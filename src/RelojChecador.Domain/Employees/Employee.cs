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

    /// <summary>Observaciones libres sobre el empleado — pensado originalmente para
    /// conservar el origen/las excepciones detectadas al importar un catálogo desde una
    /// fuente externa (Excel, WhatsApp, etc.) para auditoría futura, pero sirve para
    /// cualquier nota administrativa.</summary>
    public string? Notes { get; private set; }

    /// <summary>Sueldo fijo por semana completa (lunes a domingo, ver
    /// RelojChecador.Application.Payroll.WeekBoundary) — insumo de nómina sin ningún
    /// cálculo fiscal (ISR/IMSS), a pedido explícito del usuario. No se prorratea por
    /// faltas: eso queda fuera de alcance hasta que existan horarios esperados por
    /// empleado.
    ///
    /// Nullable a propósito: `null` significa "sueldo todavía no capturado/pendiente de
    /// confirmar" — NUNCA se asume `0` en su lugar (caso real: importación de un catálogo
    /// donde varios empleados no tenían el dato disponible en ninguna fuente). Ver
    /// WorkedHoursCalculator.CalculateWeek para cómo se refleja esto en el cálculo de
    /// nómina (no se suma como si fuera $0, se advierte explícitamente).</summary>
    public decimal? WeeklySalary { get; private set; }

    /// <summary>Tarifa fija en pesos por hora extra — null si el empleado no tiene horas
    /// extra o el usuario todavía no la capturó. Deliberadamente NO aplica la regla de
    /// Ley Federal del Trabajo (doble/triple): el usuario eligió una tarifa fija que él
    /// mismo controla en vez de que el sistema asuma una regla legal.</summary>
    public decimal? OvertimeHourlyRate { get; private set; }

    /// <summary>Hora de entrada esperada según su horario — pedido explícito del usuario
    /// ("que en Empleados me aparezcan sus horarios"). Puramente informativa/de reporte por
    /// ahora: la regla de "salida de turno" (ver
    /// RelojChecador.Application.Attendances.ShiftPunchTypeClassifier) NO depende de esto,
    /// cuenta las 7h50 desde la PRIMERA checada real del día, no desde esta hora capturada
    /// — pedido explícito también. Null = horario todavía sin capturar, nunca se asume un
    /// valor por defecto.</summary>
    public TimeOnly? ScheduledStartTime { get; private set; }

    /// <summary>Hora de salida esperada — ver <see cref="ScheduledStartTime"/>.</summary>
    public TimeOnly? ScheduledEndTime { get; private set; }

    private Employee()
    {
        // Constructor privado para EF Core.
    }

    public static Employee Create(
        EmployeeNumber number,
        string fullName,
        Guid branchId,
        DateOnly hireDate,
        decimal? weeklySalary,
        string? department = null,
        string? position = null,
        decimal? overtimeHourlyRate = null)
    {
        ArgumentNullException.ThrowIfNull(number);
        Guard.AgainstNullOrWhiteSpace(fullName, nameof(fullName));
        Guard.AgainstEmptyGuid(branchId, nameof(branchId));
        // Guard solo corre si SÍ se capturó un valor — null significa "pendiente", no es
        // un valor inválido que rechazar (ver comentario de WeeklySalary).
        if (weeklySalary is not null)
        {
            Guard.AgainstNegative(weeklySalary.Value, nameof(weeklySalary));
        }
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

    public void UpdateCompensation(decimal? weeklySalary, decimal? overtimeHourlyRate)
    {
        if (weeklySalary is not null)
        {
            Guard.AgainstNegative(weeklySalary.Value, nameof(weeklySalary));
        }
        if (overtimeHourlyRate is not null)
        {
            Guard.AgainstNegative(overtimeHourlyRate.Value, nameof(overtimeHourlyRate));
        }

        WeeklySalary = weeklySalary;
        OvertimeHourlyRate = overtimeHourlyRate;
        Touch();
    }

    /// <summary>Reemplaza las observaciones libres — texto vacío se guarda como
    /// <c>null</c> (mismo criterio que el resto de campos opcionales de texto, ver
    /// UpdateFiscalInfo).</summary>
    public void UpdateNotes(string? notes)
    {
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Touch();
    }

    /// <summary>Corrige la fecha de ingreso — normalmente se fija una sola vez al dar de
    /// alta (ver <see cref="Create"/>), pero un catálogo importado después con datos más
    /// completos (ver EmployeesViewModel.ApplyCatalogReplaceAsync) puede traer la fecha
    /// real cuando la que se usó al crear el registro localmente fue solo un placeholder
    /// (la fecha de la importación, no la fecha real de ingreso del empleado).</summary>
    public void UpdateHireDate(DateOnly hireDate)
    {
        HireDate = hireDate;
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

    /// <summary>Captura o corrige el horario esperado — ambos null para dejarlo "sin
    /// capturar" (no se puede fijar solo uno, un horario a medias no es útil para
    /// reportar).</summary>
    public void UpdateSchedule(TimeOnly? scheduledStartTime, TimeOnly? scheduledEndTime)
    {
        ScheduledStartTime = scheduledStartTime;
        ScheduledEndTime = scheduledEndTime;
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
