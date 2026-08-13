using RelojChecador.Domain.Common;

namespace RelojChecador.Domain.Identity;

/// <summary>
/// Identidad y autorización de un usuario del sistema. La autenticación real
/// (validar contraseña/token) vive fuera del Domain — en Supabase Auth y/o el
/// mecanismo de sesión local (Fase 4, tarea de autenticación). Esta entidad solo
/// modela QUIÉN es el usuario, qué rol tiene y a qué sucursales puede acceder.
/// </summary>
public sealed class User : AuditableEntity
{
    private readonly List<Guid> _branchIds = [];

    public string Username { get; private set; } = null!;
    public string? FullName { get; private set; }
    public string? Email { get; private set; }
    public RoleName Role { get; private set; }
    public bool IsActive { get; private set; } = true;

    /// <summary>Sucursales a las que este usuario tiene acceso. Se ignora cuando
    /// Role es Administrador (acceso implícito a todas).</summary>
    public IReadOnlyCollection<Guid> BranchIds => _branchIds.AsReadOnly();

    private User()
    {
        // Constructor privado para EF Core.
    }

    public static User Create(string username, RoleName role, string? fullName = null, string? email = null)
    {
        Guard.AgainstNullOrWhiteSpace(username, nameof(username));

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Username = username.Trim().ToLowerInvariant(),
            Role = role,
            FullName = fullName?.Trim(),
            Email = email?.Trim(),
            IsActive = true,
        };
        user.InitializeAuditFields();
        return user;
    }

    public void GrantBranchAccess(Guid branchId)
    {
        Guard.AgainstEmptyGuid(branchId, nameof(branchId));
        if (!_branchIds.Contains(branchId))
        {
            _branchIds.Add(branchId);
            Touch();
        }
    }

    public void RevokeBranchAccess(Guid branchId)
    {
        if (_branchIds.Remove(branchId))
        {
            Touch();
        }
    }

    public bool HasAccessToBranch(Guid branchId) =>
        Role == RoleName.Administrador || _branchIds.Contains(branchId);

    public void ChangeRole(RoleName role)
    {
        Role = role;
        Touch();
    }

    public void UpdateProfile(string? fullName, string? email)
    {
        FullName = fullName?.Trim();
        Email = email?.Trim();
        Touch();
    }

    public void Activate()
    {
        IsActive = true;
        Touch();
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }
}
