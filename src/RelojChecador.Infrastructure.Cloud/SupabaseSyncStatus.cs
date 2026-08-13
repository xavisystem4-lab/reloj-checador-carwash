namespace RelojChecador.Infrastructure.Cloud;

/// <summary>
/// Estado observable del motor de sincronización — existe porque diagnosticar "no sube
/// nada a Supabase" sin ver el archivo de logs de la máquina real resultó, en la práctica,
/// muy lento (varias rondas de capturas de pantalla pedidas al usuario). Con esto, el
/// estado actual (deshabilitado / sincronizando / última vez exitosa / último error) es
/// visible directamente en la app — ver el enlace en MainWindow footer,
/// UpdateViewModel.CloudSyncStatusMessage.
///
/// Singleton (no Scoped): un solo estado global compartido entre el
/// SupabaseSyncBackgroundService (que lo actualiza en cada ciclo) y cualquier ViewModel que
/// lo muestre — no hay "una instancia por ventana" que tenga sentido aquí.
/// </summary>
public sealed class SupabaseSyncStatus
{
    private readonly object _lock = new();

    public bool IsConfigured { get; private set; }
    public DateTime? LastAttemptAtUtc { get; private set; }
    public DateTime? LastSuccessAtUtc { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Se dispara en el hilo del BackgroundService, NO en el hilo de UI — quien
    /// escuche debe hacer su propio marshaling (Dispatcher.Invoke) antes de tocar
    /// controles.</summary>
    public event EventHandler? Changed;

    public void MarkDisabled()
    {
        lock (_lock)
        {
            IsConfigured = false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkAttemptStarted()
    {
        lock (_lock)
        {
            IsConfigured = true;
            LastAttemptAtUtc = DateTime.UtcNow;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkSuccess()
    {
        lock (_lock)
        {
            LastSuccessAtUtc = DateTime.UtcNow;
            LastError = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkFailure(string error)
    {
        lock (_lock)
        {
            LastError = error;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
