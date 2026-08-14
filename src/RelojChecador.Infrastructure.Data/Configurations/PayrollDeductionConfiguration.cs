using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelojChecador.Domain.Payroll;

namespace RelojChecador.Infrastructure.Data.Configurations;

public sealed class PayrollDeductionConfiguration : IEntityTypeConfiguration<PayrollDeduction>
{
    public void Configure(EntityTypeBuilder<PayrollDeduction> builder)
    {
        builder.ToTable("PayrollDeductions");
        builder.HasKey(d => d.Id);

        // Una fila por empleado y semana — nunca dos correcciones separadas para la misma
        // semana, siempre se actualiza la existente (ver PayrollDeduction.UpdateAmounts).
        builder.HasIndex(d => new { d.EmployeeId, d.WeekStart }).IsUnique();

        // Montos capturados a mano, sin cálculo fiscal (ver comentario de clase de
        // PayrollDeduction) — mismo criterio de precisión que Employee.WeeklySalary.
        builder.Property(d => d.IsrAmount).HasPrecision(10, 2).IsRequired();
        builder.Property(d => d.ImssAmount).HasPrecision(10, 2).IsRequired();
        builder.Property(d => d.OtherAmount).HasPrecision(10, 2).IsRequired();
        builder.Property(d => d.OtherLabel).HasMaxLength(100);
        builder.Property(d => d.Notes).HasMaxLength(500);

        builder.Property(d => d.ConcurrencyToken).IsConcurrencyToken();
    }
}
