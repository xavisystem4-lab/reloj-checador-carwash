using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelojChecador.Domain.Attendances;

namespace RelojChecador.Infrastructure.Data.Configurations;

public sealed class AttendanceConfiguration : IEntityTypeConfiguration<Attendance>
{
    public void Configure(EntityTypeBuilder<Attendance> builder)
    {
        builder.ToTable("Attendances");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.DeviceUserPin).HasMaxLength(50).IsRequired();
        builder.Property(a => a.RawPayload).IsRequired();
        builder.Property(a => a.VerifyMethod).HasConversion<int>();

        builder.HasIndex(a => a.BranchId);
        builder.HasIndex(a => a.EmployeeId);

        // Único índice que respalda la deduplicación de IAttendanceRepository.ExistsAsync:
        // la misma marcación puede llegar tanto por el monitoreo en tiempo real como por
        // una descarga manual posterior — este índice es la garantía a nivel de base de
        // datos, no solo la comprobación previa en código (que puede perder una carrera
        // entre dos inserciones concurrentes).
        builder.HasIndex(a => new { a.DeviceId, a.DeviceUserPin, a.TimestampUtc }).IsUnique();

        builder.Property(a => a.ConcurrencyToken).IsConcurrencyToken();
    }
}
