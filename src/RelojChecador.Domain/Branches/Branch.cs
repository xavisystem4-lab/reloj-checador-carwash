using RelojChecador.Domain.Common;

namespace RelojChecador.Domain.Branches;

/// <summary>
/// Una sucursal del carwash. El modelo es single-tenant (una sola empresa, varias
/// sucursales) — no existe un nivel "Empresa" por encima de Branch.
/// </summary>
public sealed class Branch : AuditableEntity
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? LegalEntityName { get; private set; }
    public string? Address { get; private set; }

    /// <summary>Identificador de zona horaria IANA, p. ej. "America/Tijuana" (Mexicali, B.C.).</summary>
    public string TimeZoneId { get; private set; } = null!;

    /// <summary>Empleado responsable de la sucursal. Referencia por Id, sin navegación directa
    /// a Employee, para no acoplar los dos agregados.</summary>
    public Guid? ManagerEmployeeId { get; private set; }

    public bool IsActive { get; private set; } = true;

    private Branch()
    {
        // Constructor privado para EF Core.
    }

    public static Branch Create(
        string code,
        string name,
        string timeZoneId,
        string? legalEntityName = null,
        string? address = null)
    {
        Guard.AgainstNullOrWhiteSpace(code, nameof(code));
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Guard.AgainstNullOrWhiteSpace(timeZoneId, nameof(timeZoneId));

        var branch = new Branch
        {
            Id = Guid.CreateVersion7(),
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            TimeZoneId = timeZoneId.Trim(),
            LegalEntityName = legalEntityName?.Trim(),
            Address = address?.Trim(),
            IsActive = true,
        };
        branch.InitializeAuditFields();
        return branch;
    }

    public void Rename(string name)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Name = name.Trim();
        Touch();
    }

    public void UpdateLegalInfo(string? legalEntityName, string? address)
    {
        LegalEntityName = legalEntityName?.Trim();
        Address = address?.Trim();
        Touch();
    }

    public void UpdateTimeZone(string timeZoneId)
    {
        Guard.AgainstNullOrWhiteSpace(timeZoneId, nameof(timeZoneId));
        TimeZoneId = timeZoneId.Trim();
        Touch();
    }

    public void AssignManager(Guid? managerEmployeeId)
    {
        ManagerEmployeeId = managerEmployeeId;
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
