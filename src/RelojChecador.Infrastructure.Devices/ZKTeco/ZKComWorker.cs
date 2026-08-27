using System.Collections.Concurrent;

namespace RelojChecador.Infrastructure.Devices.ZKTeco;

/// <summary>
/// Ejecuta TODO el trabajo contra el objeto COM del SDK de ZKTeco (<c>zkemkeeper.dll</c>)
/// siempre desde un ÚNICO hilo dedicado — esta es la causa raíz real del cierre inesperado
/// reportado por el usuario al usar "Enviar empleados al reloj" con varias decenas de
/// empleados: antes de esta clase, CADA llamada al SDK (<c>Connect_Net</c>,
/// <c>SSR_SetUserInfo</c>, <c>SSR_GetGeneralLogData</c>, etc., ver
/// <see cref="ZKTecoDeviceAdapter"/>) corría en su propio <c>Task.Run(...)</c> — es decir,
/// en CUALQUIER hilo que el ThreadPool de .NET decidiera usar en ese momento, potencialmente
/// distinto del hilo que creó el objeto COM y distinto entre una llamada y la siguiente.
///
/// <c>zkemkeeper.dll</c> es un componente COM clásico de apartamento único (threading model
/// "Apartment", típico de un ActiveX de la era VB6): solo es seguro invocarlo desde el
/// MISMO hilo (apartamento STA) que lo creó. Invocarlo desde hilos/apartamentos distintos es
/// una violación de threading COM que .NET no siempre convierte en una excepción
/// administrada capturable — en el peor caso corrompe memoria dentro del componente nativo
/// de 32 bits y el PROCESO COMPLETO termina por un fallo del sistema operativo (equivalente
/// a <see cref="AccessViolationException"/>, que además nunca es capturable en .NET moderno,
/// por diseño explícito del CLR desde .NET Framework 4) sin pasar por NINGÚN manejador de
/// excepciones administradas — ni <c>Dispatcher.UnhandledException</c> ni
/// <c>AppDomain.UnhandledException</c> (ver App.xaml.cs) llegan siquiera a enterarse. Esto
/// coincide exactamente con el síntoma reportado: "la aplicación se cierra", sin ningún
/// diálogo de error ni entrada en el log de crash — justo lo que se espera de un fallo
/// nativo no administrado, muy distinto de una excepción .NET normal.
///
/// El Timer de monitoreo en tiempo real (<c>PollForNewPunchesAsync</c>, cada 3s) agravaba
/// esto de verdad: mientras el usuario enviaba 55 empleados seguidos desde "Enviar empleados
/// al reloj", ese Timer seguía tocando el MISMO objeto COM desde SU PROPIO hilo del
/// ThreadPool en paralelo — no solo llamadas secuenciales desde hilos distintos entre sí,
/// sino acceso genuinamente CONCURRENTE al mismo objeto de apartamento único. Con esta
/// clase, esa llamada del Timer y las 55 llamadas del envío masivo terminan todas en la
/// misma cola de UN SOLO hilo, así que se ejecutan una por una, nunca al mismo tiempo.
/// </summary>
internal sealed class ZKComWorker : IDisposable
{
    private readonly object _lifecycleLock = new();
    private BlockingCollection<Action>? _queue;

    /// <summary>Encola <paramref name="operation"/> para que corra en el único hilo
    /// dedicado y espera su resultado, con un límite de tiempo real. Igual que el diseño
    /// anterior (RunWithTimeoutAsync en ZKTecoDeviceAdapter): no existe forma segura de
    /// abortar una llamada nativa ya en marcha (<c>Thread.Abort</c> no existe en .NET
    /// moderno) — si <paramref name="timeout"/> se cumple primero, quien llamó deja de
    /// esperar de inmediato (la UI queda libre de inmediato) y <see cref="AbandonThread"/>
    /// retira el hilo dedicado de circulación: la llamada nativa que lo tenga ocupado sigue
    /// corriendo de fondo, huérfana, hasta que el SDK finalmente responda o el sistema
    /// operativo agote su propio timeout de TCP, pero la SIGUIENTE operación arranca un hilo
    /// (y, del lado de <see cref="ZKTecoDeviceAdapter"/>, un objeto COM) completamente
    /// nuevo — nunca comparte el objeto COM de un hilo abandonado entre dos hilos a la vez,
    /// que es justo la violación de threading que esta clase existe para evitar.</summary>
    public async Task<T> RunAsync<T>(Func<T> operation, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lifecycleLock)
        {
            var queue = EnsureThreadLocked();
            queue.Add(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    tcs.TrySetResult(operation());
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    tcs.TrySetException(ex);
                }
            });
        }

        var timeoutTask = Task.Delay(timeout, cancellationToken);
        var finished = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);
        if (finished == tcs.Task)
        {
            return await tcs.Task.ConfigureAwait(false);
        }

        // finished == timeoutTask, pero eso pasa por DOS motivos muy distintos que
        // Task.Delay no distingue por sí solo: (a) se cumplió el tiempo de espera de verdad
        // — la llamada nativa lleva más de lo normal, probablemente atascada; o (b) quien
        // llamó canceló el CancellationToken (Task.Delay con un token cancelado también
        // "termina", en estado Canceled). Son casos MUY distintos: una cancelación pedida
        // por el usuario ("Cancelar" en el diálogo de progreso) no significa que el hilo
        // esté atascado — la operación en curso puede terminar perfectamente bien un
        // instante después, así que NO se abandona el hilo dedicado ni se fuerza a
        // recrear el objeto COM; solo un timeout real (el token sigue sin cancelarse)
        // sugiere que la llamada nativa está en verdad colgada.
        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        AbandonThread();
        throw new TimeoutException($"El dispositivo no respondió en {timeout.TotalSeconds:0}s.");
    }

    /// <summary>Retira el hilo dedicado actual de circulación sin esperar a que termine —
    /// ver el comentario de <see cref="RunAsync{T}"/>. La siguiente llamada crea uno nuevo
    /// desde cero (<see cref="EnsureThreadLocked"/>).</summary>
    private void AbandonThread()
    {
        lock (_lifecycleLock)
        {
            _queue?.CompleteAdding();
            _queue = null;
        }
    }

    /// <summary>Debe llamarse con <see cref="_lifecycleLock"/> ya tomado.</summary>
    private BlockingCollection<Action> EnsureThreadLocked()
    {
        if (_queue is not null)
        {
            return _queue;
        }

        var queue = new BlockingCollection<Action>();
        var thread = new Thread(() =>
        {
            // GetConsumingEnumerable bloquea este hilo esperando trabajo hasta que se llame
            // CompleteAdding (AbandonThread/Dispose) — el hilo termina solo entonces.
            foreach (var action in queue.GetConsumingEnumerable())
            {
                action();
            }
        })
        {
            IsBackground = true,
            Name = "ZKTeco-SDK",
        };

        // SetApartmentState solo es válido en Windows — en cualquier otra plataforma
        // EnsureComObject ya falla antes de que esto importe (ver ZKTecoDeviceAdapter), pero
        // el hilo en sí se crea igual sin importar el sistema operativo, así que se protege
        // aquí para no arriesgar una PlatformNotSupportedException si esta clase llegara a
        // instanciarse fuera de Windows (p. ej. en pruebas automatizadas).
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }

        thread.Start();

        _queue = queue;
        return queue;
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            _queue?.CompleteAdding();
            _queue = null;
        }
    }
}
