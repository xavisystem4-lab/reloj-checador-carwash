using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelojChecador.Domain.Branches;

namespace RelojChecador.Infrastructure.Data.Configurations;

public sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branches");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Code).HasMaxLength(20).IsRequired();
        builder.HasIndex(b => b.Code).IsUnique();

        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();
        builder.Property(b => b.LegalEntityName).HasMaxLength(200);
        builder.Property(b => b.Address).HasMaxLength(400);
        builder.Property(b => b.TimeZoneId).HasMaxLength(100).IsRequired();

        builder.Property(b => b.ConcurrencyToken).IsConcurrencyToken();
    }
}
