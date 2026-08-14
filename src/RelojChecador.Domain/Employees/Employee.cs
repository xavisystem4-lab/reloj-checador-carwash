using RelojChecador.Domain.Common;

namespace RelojChecador.Domain.Employees;

/// <summary>
/// Un empleado de una sucursal. Los campos Rfc/Curp/Nss están preparados para el
/// contexto de nómina en México (Mexicali, B.C.) pero no se usan aún en ningún cálculo
/// fiscal (ISR/IMSS) — eso sigue fuera de alcance hasta confirmar las tablas y reglas
/// vigentes. WeeklySalary/OvertimeHourlyRate sí se usan (ver
/// RelojChecador.Application.Payroll.WorkedHoursCalculator), pero solo como insumo de
/// sueldo bruto, sin ninguna retención.
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

    /// <summary>Sueldo fijo por semana completa (lunes a domingo, ver
    /// RelojChecador.Application.Payroll.WeekBoundary) — insumo de nómina sin ningún
    /// cálculo fiscal (ISR/IMSS), a pedido explícito del usuario. No se prorratea por
    /// faltas: eso queda fuera de alcance hasta que existan horarios esperados por
    /// empleado.</summary>
    public decimal WeeklySalary { get; private set; }

    /// <summary>Tarifa fija en pesos por hora extra — null si el empleado no tiene horas
    /// extra o el usuario todavía no la capturó. Deliberadamente NO aplica la regla de
    /// Ley Federal del Trabajo (doble/triple): el usuario eligió una tarifa fija que él
    /// mismo controla en vez de que el sistema asuma una regla legal.</summary>
    public decimal? OvertimeHourlyRate { get; private set; }

    private Employee()
    {
        // Constructor privado para EF Core.
    }

    public static Employee Create(
        EmployeeNumber number,
        string fullName,
        Guid branchId,
        DateOnly hireDate,
        decimal weeklySalary,
        string? department = null,
        string? position = null,
        decimal? overtimeHourlyRate = null)
    {
        ArgumentNullException.ThrowIfNull(number);
        Guard.AgainstNullOrWhiteSpace(fullName, nameof(fullName));
        Guard.AgainstEmptyGuid(branchId, nameof(branchId));
        Guard.AgainstNegative(weeklySalary, nameof(weeklySalary));
        if (overtimeHourlyRate is not null)
        {
            Guard.AgainstNegative(overtimeHourlyRate.Value, nameof(overtimeHourlyRate));
        }

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
            WeeklySalary = weeklySalary,
            OvertimeHourlyRate = overtimeHourlyRate,
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

    public void UpdateCompensation(decimal weeklySalary, decimal? overtimeHourlyRate)
    {
        Guard.AgainstNegative(weeklySalary, nameof(weeklySalary));
        if (overtimeHourlyRate is not null)
        {
            Guard.AgainstNegative(overtimeHourlyRate.Value, nameof(overtimeHourlyRate));
        }

        WeeklySalary = weeklySalary;
        OvertimeHourlyRate = overtimeHourlyRate;
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

    /// <summary>Corrige el número de empleado tras el alta (p. ej. error de captura). El
    /// índice único sobre Number (ver EmployeeConfiguration) sigue siendo la defensa real
    /// contra duplicados — esto no lo verifica aquí, solo lo deja disponible para editar.</summary>
    public void ChangeNumber(EmployeeNumber newNumber)
    {
        ArgumentNullException.ThrowIfNull(newNumber);
        Number = newNumber;
        Touch();
    }
}
