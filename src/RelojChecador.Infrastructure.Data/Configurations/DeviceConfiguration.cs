using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelojChecador.Domain.Devices;

namespace RelojChecador.Infrastructure.Data.Configurations;

public sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("Devices");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Brand).HasMaxLength(50).IsRequired();
        builder.Property(d => d.Model).HasMaxLength(50).IsRequired();
        builder.Property(d => d.SerialNumber).HasMaxLength(50);
        builder.Property(d => d.MacAddress).HasMaxLength(17);
        builder.Property(d => d.IpAddress).HasMaxLength(45).IsRequired(); // 45 = longitud máxima de IPv6
        builder.Property(d => d.MachineNumber).HasMaxLength(20);
        builder.Property(d => d.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.Property(d => d.FirmwareVersion).HasMaxLength(50);
        builder.Property(d => d.CredentialReference).HasMaxLength(200);

        builder.HasIndex(d => d.BranchId);
        builder.Property(d => d.ConcurrencyToken).IsConcurrencyToken();
    }
}
