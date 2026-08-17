using RelojChecador.Application.Common;

namespace RelojChecador.Application.Devices;

/// <summary>
/// Guarda la clave de comunicación de un dispositivo (algunos relojes ZK/EBKN la exigen
/// para aceptar Connect_Net — reportado por el usuario con un equipo real que no conectaba
/// sin ella) fuera de la base de datos local, en el almacén de credenciales del sistema
/// operativo (Windows Credential Manager). <see cref="Domain.Devices.Device.CredentialReference"/>
/// solo guarda la CLAVE hacia esa entrada, nunca el secreto en sí — ver
/// RelojChecador.Infrastructure.Security.WindowsCredentialStore.
/// </summary>
public interface IDeviceCredentialStore
{
    /// <summary>Guarda (o reemplaza) la clave de comunicación bajo <paramref name="reference"/>.</summary>
    Task<Result> SaveAsync(string reference, string communicationKey, CancellationToken cancellationToken = default);

    /// <summary>Null si no hay ninguna clave guardada bajo esa referencia (nunca lanza por
    /// "no encontrado" — es un caso normal, la mayoría de los dispositivos no necesitan clave).</summary>
    Task<string?> GetAsync(string reference, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(string reference, CancellationToken cancellationToken = default);
}
