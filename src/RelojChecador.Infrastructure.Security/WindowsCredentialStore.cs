using System.ComponentModel;
using System.Runtime.InteropServices;
using RelojChecador.Application.Common;
using RelojChecador.Application.Devices;

namespace RelojChecador.Infrastructure.Security;

/// <summary>
/// Implementación real de <see cref="IDeviceCredentialStore"/> usando Windows Credential
/// Manager (las mismas APIs nativas — CredWrite/CredRead/CredDelete de advapi32.dll — que
/// usa Windows para guardar contraseñas de red, RDP, etc.). No hay dependencia NuGet nueva
/// a propósito, mismo criterio que zkemkeeper.dll: son 3 funciones de la API de Win32, un
/// wrapper propio de ~80 líneas es más simple y transparente que traer un paquete completo
/// solo para esto.
///
/// Cada entrada se guarda con TargetName = "RelojChecador/{reference}" y
/// Type = CRED_TYPE_GENERIC, Persist = LOCAL_MACHINE (sobrevive a reinicios y no depende de
/// que el usuario de Windows tenga el perfil "roaming" habilitado — la PC de la sucursal es
/// de un solo uso, no importa qué usuario de Windows tenga la sesión iniciada).
/// </summary>
public sealed class WindowsCredentialStore : IDeviceCredentialStore
{
    private const string TargetPrefix = "RelojChecador/";
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;

    public Task<Result> SaveAsync(string reference, string communicationKey, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(Result.Failure(Error.Unexpected("El almacén de credenciales solo está disponible en Windows.")));
        }

        var target = TargetPrefix + reference;
        var secretBytes = System.Text.Encoding.Unicode.GetBytes(communicationKey);
        var blobHandle = Marshal.AllocHGlobal(secretBytes.Length);
        try
        {
            Marshal.Copy(secretBytes, 0, blobHandle, secretBytes.Length);

            var credential = new CREDENTIAL
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)secretBytes.Length,
                CredentialBlob = blobHandle,
                Persist = CredPersistLocalMachine,
                UserName = "RelojChecador",
            };

            bool ok = CredWrite(ref credential, 0);
            if (!ok)
            {
                var error = new Win32Exception(Marshal.GetLastWin32Error());
                return Task.FromResult(Result.Failure(Error.Unexpected($"No se pudo guardar la clave de comunicación: {error.Message}")));
            }

            return Task.FromResult(Result.Success());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Task.FromResult(Result.Failure(Error.Unexpected($"No se pudo guardar la clave de comunicación: {ex.Message}")));
        }
        finally
        {
            Marshal.FreeHGlobal(blobHandle);
        }
    }

    public Task<string?> GetAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<string?>(null);
        }

        var target = TargetPrefix + reference;
        bool ok = CredRead(target, CredTypeGeneric, 0, out var credentialPtr);
        if (!ok)
        {
            // ERROR_NOT_FOUND (1168) es el caso normal — la mayoría de los dispositivos no
            // tienen clave configurada. Cualquier otro error tampoco debe tumbar la pantalla
            // de Dispositivos: simplemente se conecta sin clave y, si el equipo la exige de
            // verdad, Connect_Net lo reportará como fallo de conexión igual que cualquier
            // otra causa.
            return Task.FromResult<string?>(null);
        }

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return Task.FromResult<string?>(null);
            }

            var secretBytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, secretBytes, 0, secretBytes.Length);
            return Task.FromResult<string?>(System.Text.Encoding.Unicode.GetString(secretBytes));
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    public Task<Result> DeleteAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(Result.Failure(Error.Unexpected("El almacén de credenciales solo está disponible en Windows.")));
        }

        var target = TargetPrefix + reference;
        bool ok = CredDelete(target, CredTypeGeneric, 0);
        if (!ok)
        {
            var lastError = Marshal.GetLastWin32Error();
            // ERROR_NOT_FOUND (1168): ya no existía — no es un error real desde el punto de
            // vista de quien llama ("bórrala si existe"), se trata como éxito.
            if (lastError == 1168)
            {
                return Task.FromResult(Result.Success());
            }

            var error = new Win32Exception(lastError);
            return Task.FromResult(Result.Failure(Error.Unexpected($"No se pudo borrar la clave de comunicación: {error.Message}")));
        }

        return Task.FromResult(Result.Success());
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree(IntPtr credentialPtr);
}
