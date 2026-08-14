using RelojChecador.Domain.Common;

namespace RelojChecador.Domain.Payroll;

/// <summary>
/// Deducciones de nómina capturadas MANUALMENTE por el usuario para un empleado en una
/// semana específica (lunes-domingo, ver <see cref="RelojChecador.Application.Payroll.WeekBoundary"/>).
/// El sistema NUNCA calcula ISR/IMSS por su cuenta — decisión explícita del usuario tras
/// preguntarle por el régimen fiscal y las tablas a usar ("todo se pondrá manualmente"):
/// las tasas/tablas fiscales cambian cada año y un error aquí tiene consecuencias reales
/// para el negocio, así que la app solo guarda el número que el usuario (o su contador) ya
/// calculó por fuera — nunca inventa ni aplica ninguna regla fiscal. Ver también el
/// comentario de clase de <see cref="RelojChecador.Application.Payroll.WeeklyPayrollSummary"/>.
///
/// Una fila por (EmployeeId, WeekStart) — nunca recurrente: ISR/IMSS varían cada semana
/// según lo realmente devengado (horas extra incluidas), así que no tendría sentido copiar
/// un valor de una semana a la siguiente automáticamente.
///
/// "OtherAmount"/"OtherLabel" es un único descuento adicional de etiqueta libre (en vez de
/// columnas separadas para INFONAVIT/faltas/préstamos/etc.) — mantiene el modelo simple sin
/// tener que anticipar cada tipo de descuento que un negocio pequeño pueda necesitar.
/// </summary>
public sealed class PayrollDeduction : AuditableEntity
{
    public Guid EmployeeId { get; private set; }
    public DateOnly WeekStart { get; private set; }
    public decimal IsrAmount { get; private set; }
    public decimal ImssAmount { get; private set; }
    public decimal OtherAmount { get; private set; }
    public string? OtherLabel { get; private set; }
    public string? Notes { get; private set; }

    private PayrollDeduction()
    {
        // Constructor privado para EF Core.
    }

    public static PayrollDeduction Create(Guid employeeId, DateOnly weekStart)
    {
        Guard.AgainstEmptyGuid(employeeId, nameof(employeeId));

        var deduction = new PayrollDeduction
        {
            Id = Guid.CreateVersion7(),
            EmployeeId = employeeId,
            WeekStart = weekStart,
        };
        deduction.InitializeAuditFields();
        return deduction;
    }

    /// <summary>Reemplaza los montos/etiqueta/notas capturados — se llama tanto la primera
    /// vez (justo tras <see cref="Create"/>) como en cualquier corrección posterior de la
    /// misma semana, nunca se crea una fila nueva para "corregir" un monto ya
    /// capturado.</summary>
    public void UpdateAmounts(decimal isrAmount, decimal imssAmount, decimal otherAmount, string? otherLabel, string? notes)
    {
        Guard.AgainstNegative(isrAmount, nameof(isrAmount));
        Guard.AgainstNegative(imssAmount, nameof(imssAmount));
        Guard.AgainstNegative(otherAmount, nameof(otherAmount));

        IsrAmount = isrAmount;
        ImssAmount = imssAmount;
        OtherAmount = otherAmount;
        OtherLabel = string.IsNullOrWhiteSpace(otherLabel) ? null : otherLabel.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Touch();
    }
}
