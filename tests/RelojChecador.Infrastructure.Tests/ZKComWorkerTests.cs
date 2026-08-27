using RelojChecador.Infrastructure.Devices.ZKTeco;

namespace RelojChecador.Infrastructure.Tests;

/// <summary>
/// Prueba la corrección de threading de <see cref="ZKComWorker"/> — la causa raíz real del
/// cierre inesperado reportado por el usuario al usar "Enviar empleados al reloj" con
/// varias decenas de empleados seguidos (ver el comentario de clase de ZKComWorker para la
/// explicación completa). No prueba nada del SDK de ZKTeco en sí (eso solo existe en
/// Windows real, ver ZKTecoDeviceAdapter) — solo el mecanismo genérico de "todo el trabajo
/// siempre en el mismo hilo dedicado, con timeout real y sin abandonar el hilo por una
/// simple cancelación".
/// </summary>
public class ZKComWorkerTests
{
    [Fact]
    public async Task RunAsync_DevuelveElResultadoDeLaOperacion()
    {
        using var worker = new ZKComWorker();

        var result = await worker.RunAsync(() => 42, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task RunAsync_EjecutaSiempreEnElMismoHiloDedicado_DistintoDelHiloQueLlama()
    {
        using var worker = new ZKComWorker();
        var callingThreadId = Environment.CurrentManagedThreadId;

        var firstThreadId = await worker.RunAsync(() => Environment.CurrentManagedThreadId, TimeSpan.FromSeconds(5), CancellationToken.None);
        var secondThreadId = await worker.RunAsync(() => Environment.CurrentManagedThreadId, TimeSpan.FromSeconds(5), CancellationToken.None);
        var thirdThreadId = await worker.RunAsync(() => Environment.CurrentManagedThreadId, TimeSpan.FromSeconds(5), CancellationToken.None);

        // Exactamente el escenario que causaba el cierre real: antes, cada llamada corría
        // en un Task.Run separado — potencialmente un hilo distinto cada vez. Aquí las tres
        // deben caer en EL MISMO hilo, y ninguna en el hilo que llamó (la UI, en la app real).
        Assert.Equal(firstThreadId, secondThreadId);
        Assert.Equal(firstThreadId, thirdThreadId);
        Assert.NotEqual(callingThreadId, firstThreadId);
    }

    [Fact]
    public async Task RunAsync_SiSeAgotaElTiempo_LanzaTimeoutExceptionYElSiguienteUsaUnHiloNuevo()
    {
        using var worker = new ZKComWorker();

        var beforeTimeoutThreadId = await worker.RunAsync(() => Environment.CurrentManagedThreadId, TimeSpan.FromSeconds(5), CancellationToken.None);

        // Operación deliberadamente más lenta que el timeout — simula una llamada nativa
        // realmente colgada (p. ej. Connect_Net contra un reloj inalcanzable).
        var stuckOperationCompleted = new ManualResetEventSlim(false);
        await Assert.ThrowsAsync<TimeoutException>(() => worker.RunAsync(
            () =>
            {
                Thread.Sleep(500);
                stuckOperationCompleted.Set();
                return 0;
            },
            TimeSpan.FromMilliseconds(50),
            CancellationToken.None));

        var afterTimeoutThreadId = await worker.RunAsync(() => Environment.CurrentManagedThreadId, TimeSpan.FromSeconds(5), CancellationToken.None);

        // La siguiente operación debe correr en un hilo NUEVO — nunca reutilizar el hilo
        // que quedó abandonado (todavía "vivo" de fondo, ver stuckOperationCompleted) — esto
        // es justo lo que evita la violación de threading COM que causaba el cierre.
        Assert.NotEqual(beforeTimeoutThreadId, afterTimeoutThreadId);

        // Limpieza: espera a que la operación huérfana termine antes de que el test siga
        // (no debería tardar más de medio segundo real).
        Assert.True(stuckOperationCompleted.Wait(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task RunAsync_SiSeCancela_LanzaOperationCanceledExceptionSinAbandonarElHilo()
    {
        using var worker = new ZKComWorker();

        var beforeCancelThreadId = await worker.RunAsync(() => Environment.CurrentManagedThreadId, TimeSpan.FromSeconds(5), CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Puede llegar como TaskCanceledException (si gana la ruta del propio
        // TaskCompletionSource) u OperationCanceledException llano (si gana la ruta del
        // timeout distinguido como cancelación, ver RunAsync) — cuál de las dos gana es una
        // carrera entre dos tasks que se resuelven casi al mismo tiempo, así que la prueba
        // solo exige el tipo base común, que es lo único que de verdad importa: todo el
        // código que llama a esto (ZKTecoDeviceAdapter.RunOnComThreadAsync) atrapa
        // OperationCanceledException, nunca el tipo derivado específico.
        await Assert.ThrowsAsync<OperationCanceledException>(() => worker.RunAsync(
            () => Environment.CurrentManagedThreadId, TimeSpan.FromSeconds(5), cts.Token));

        var afterCancelThreadId = await worker.RunAsync(() => Environment.CurrentManagedThreadId, TimeSpan.FromSeconds(5), CancellationToken.None);

        // A diferencia de un timeout real, cancelar NO debe abandonar el hilo — sigue siendo
        // el mismo de antes, no uno nuevo (ver comentario de ZKComWorker.RunAsync sobre por
        // qué esta distinción importa: cancelar no significa que el hilo esté atascado).
        Assert.Equal(beforeCancelThreadId, afterCancelThreadId);
    }

    [Fact]
    public async Task RunAsync_SiLaOperacionLanzaExcepcion_LaPropagaSinTumbarElHilo()
    {
        using var worker = new ZKComWorker();

        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.RunAsync(
            int () => throw new InvalidOperationException("falla simulada"), TimeSpan.FromSeconds(5), CancellationToken.None));

        // El hilo dedicado debe seguir sano después de una excepción normal de la operación
        // — no es un timeout ni una cancelación, así que no hay motivo para abandonarlo.
        var afterFailureResult = await worker.RunAsync(() => 7, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.Equal(7, afterFailureResult);
    }
}
