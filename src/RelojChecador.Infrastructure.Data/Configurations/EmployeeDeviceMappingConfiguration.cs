using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelojChecador.Domain.EmployeeDeviceMappings;

namespace RelojChecador.Infrastructure.Data.Configurations;

public sealed class EmployeeDeviceMappingConfiguration : IEntityTypeConfiguration<EmployeeDeviceMapping>
{
    public void Configure(EntityTypeBuilder<EmployeeDeviceMapping> builder)
    {
        builder.ToTable("EmployeeDeviceMappings");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.DeviceUserPin).HasMaxLength(50).IsRequired();

        // Un mismo empleado no puede tener dos PINs distintos en el mismo dispositivo,
        // y un PIN no puede apuntar a dos empleados distintos en el mismo dispositivo.
        builder.HasIndex(m => new { m.DeviceId, m.EmployeeId }).IsUnique();
        builder.HasIndex(m => new { m.DeviceId, m.DeviceUserPin }).IsUnique();
    }
}
