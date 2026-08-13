using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelojChecador.Infrastructure.Data.Sync;

namespace RelojChecador.Infrastructure.Data.Configurations;

public sealed class SyncCursorRecordConfiguration : IEntityTypeConfiguration<SyncCursorRecord>
{
    public void Configure(EntityTypeBuilder<SyncCursorRecord> builder)
    {
        builder.ToTable("SyncCursors");
        builder.HasKey(c => c.EntityType);
        builder.Property(c => c.EntityType).HasMaxLength(100);
    }
}
