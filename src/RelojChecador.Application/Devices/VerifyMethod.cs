namespace RelojChecador.Application.Devices;

/// <summary>Método con el que el dispositivo verificó al empleado en una marcación.
/// "Unknown" es válido: no todos los equipos reportan este dato.</summary>
public enum VerifyMethod
{
    Unknown = 0,
    Fingerprint = 1,
    Password = 2,
    Card = 3,
    Face = 4,
}
