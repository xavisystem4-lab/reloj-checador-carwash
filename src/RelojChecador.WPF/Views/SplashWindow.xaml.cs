using System.Windows;
using System.Windows.Media.Animation;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Pantalla de bienvenida con el logotipo animado de Drive In Car Wash (v1.71.0). La usa
/// App.OnStartup así:
/// 1. <see cref="PlayIntroAsync"/> — se muestra y se espera a que termine la intro.
/// 2. App hace el arranque pesado (host, migraciones) en el hilo de la UI, igual que
///    siempre; mientras tanto la pantalla queda quieta con el logo ya completo.
/// 3. App muestra MainWindow y llama a <see cref="CloseWithFadeAsync"/>.
/// El orden importa: esta ventana se cierra DESPUÉS de mostrar MainWindow — con el
/// ShutdownMode por defecto (OnLastWindowClose), cerrar la única ventana abierta
/// terminaría la app.
/// </summary>
public partial class SplashWindow : Window
{
    /// <summary>Tope de seguridad: si por cualquier motivo el Storyboard no avisa que
    /// terminó (p. ej. la ventana no llegó a renderizar en un escritorio remoto), el
    /// arranque sigue igual en vez de quedarse esperando para siempre.</summary>
    private static readonly TimeSpan IntroTimeout = TimeSpan.FromSeconds(3);

    public SplashWindow()
    {
        InitializeComponent();
    }

    /// <summary>Muestra la ventana y regresa cuando la animación de entrada terminó.</summary>
    public async Task PlayIntroAsync()
    {
        var finished = new TaskCompletionSource();
        var storyboard = (Storyboard)FindResource("IntroStoryboard");
        storyboard.Completed += (_, _) => finished.TrySetResult();

        Show();
        storyboard.Begin(this);

        await Task.WhenAny(finished.Task, Task.Delay(IntroTimeout));
        StatusText.Text = "Cargando datos…";
    }

    /// <summary>Desvanece la ventana y la cierra. Nunca lanza: un fallo aquí no debe
    /// afectar a la app ya abierta.</summary>
    public async Task CloseWithFadeAsync()
    {
        try
        {
            var finished = new TaskCompletionSource();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(280));
            fade.Completed += (_, _) => finished.TrySetResult();
            BeginAnimation(OpacityProperty, fade);
            await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        }
        catch
        {
            // Solo es estética — se cierra igual abajo.
        }

        Close();
    }
}
