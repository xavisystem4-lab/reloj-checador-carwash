using RelojChecador.Domain.Common;

namespace RelojChecador.Domain.Employees;

/// <summary>
/// Un empleado de una sucursal. Los campos Rfc/Curp/Nss están preparados para el
/// contexto de nómina en México (Mexicali, B.C.) pero no se usan aún en ningún cálculo
/// fiscal — eso queda para la Fase 6, una vez confirmadas las tablas y reglas vigentes.
/// </summary>
public sealed class Employee : AuditableEntity
{
    public EmployeeNumber Number { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public Guid BranchId { get; private set; }
    public string? Department { get; private set; }
    public string? Position { get; private set; }
    public DateOnly HireDate { get; private set; }
    public EmploymentStatus Status { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }

    // Campos preparados para nómina en México — ver comentario de la clase.
    public string? Rfc { get; private set; }
    public string? Curp { get; private set; }
    public string? Nss { get; private set; }

    private Employee()
    {
        // Constructor privado para EF Core.
    }

    public static Employee Create(
        EmployeeNumber number,
        string fullName,
        Guid branchId,
        DateOnly hireDate,
        string? department = null,
        string? position = null)
    {
        ArgumentNullException.ThrowIfNull(number);
        Guard.AgainstNullOrWhiteSpace(fullName, nameof(fullName));
        Guard.AgainstEmptyGuid(branchId, nameof(branchId));

        var employee = new Employee
        {
            Id = Guid.CreateVersion7(),
            Number = number,
            FullName = fullName.Trim(),
            BranchId = branchId,
            Department = department?.Trim(),
            Position = position?.Trim(),
            HireDate = hireDate,
            Status = EmploymentStatus.Active,
        };
        employee.InitializeAuditFields();
        return employee;
    }

    public void UpdatePersonalInfo(string fullName, string? department, string? position)
    {
        Guard.AgainstNullOrWhiteSpace(fullName, nameof(fullName));
        FullName = fullName.Trim();
        Department = department?.Trim();
        Position = position?.Trim();
        Touch();
    }

    public void UpdateContact(string? phone, string? email)
    {
        Phone = phone?.Trim();
        Email = email?.Trim();
        Touch();
    }

    public void UpdateFiscalInfo(string? rfc, string? curp, string? nss)
    {
        Rfc = rfc?.Trim().ToUpperInvariant();
        Curp = curp?.Trim().ToUpperInvariant();
        Nss = nss?.Trim();
        Touch();
    }

    public void TransferToBranch(Guid newBranchId)
    {
        Guard.AgainstEmptyGuid(newBranchId, nameof(newBranchId));
        BranchId = newBranchId;
        Touch();
    }

    public void ChangeStatus(EmploymentStatus newStatus)
    {
        Status = newStatus;
        Touch();
    }
}
