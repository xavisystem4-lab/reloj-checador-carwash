using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelojChecador.Domain.Employees;

namespace RelojChecador.Infrastructure.Data.Configurations;

public sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees");
        builder.HasKey(e => e.Id);

        // EmployeeNumber es un value object de una sola propiedad string: se guarda
        // como columna plana mediante conversión explícita, no como tabla aparte.
        builder.Property(e => e.Number)
            .HasConversion(number => number.Value, value => EmployeeNumber.Create(value))
            .HasColumnName("Number")
            .HasMaxLength(20)
            .IsRequired();
        builder.HasIndex(e => e.Number).IsUnique();

        builder.Property(e => e.FullName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Department).HasMaxLength(100);
        builder.Property(e => e.Position).HasMaxLength(100);
        builder.Property(e => e.Phone).HasMaxLength(30);
        builder.Property(e => e.Email).HasMaxLength(200);
        builder.Property(e => e.Rfc).HasMaxLength(13);
        builder.Property(e => e.Curp).HasMaxLength(18);
        builder.Property(e => e.Nss).HasMaxLength(11);
        builder.Property(e => e.Notes).HasMaxLength(1000);

        // Insumo de nómina sin cálculo fiscal (ver comentario de clase de Employee) —
        // HasPrecision documenta el rango esperado; SQLite no lo fuerza estrictamente,
        // pero Postgres (Supabase) sí, así que ambos lados quedan consistentes.
        // WeeklySalary YA NO es .IsRequired(): null = sueldo pendiente de captura, un
        // estado real distinto de "$0" (caso real: importación de catálogo con sueldos
        // desconocidos en algunas fuentes — nunca se debe inventar el valor).
        builder.Property(e => e.WeeklySalary).HasPrecision(10, 2);
        builder.Property(e => e.OvertimeHourlyRate).HasPrecision(10, 2);

        builder.HasIndex(e => e.BranchId);
        builder.Property(e => e.ConcurrencyToken).IsConcurrencyToken();
    }
}
