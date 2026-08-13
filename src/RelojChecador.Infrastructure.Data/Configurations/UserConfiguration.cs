using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelojChecador.Domain.Identity;

namespace RelojChecador.Infrastructure.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Username).HasMaxLength(100).IsRequired();
        builder.HasIndex(u => u.Username).IsUnique();
        builder.Property(u => u.FullName).HasMaxLength(200);
        builder.Property(u => u.Email).HasMaxLength(200);

        // Colección de sucursales asignadas: para el número de sucursales esperado
        // (4-15) una columna CSV sobre el campo privado es más simple que una tabla de
        // unión y evita acoplar el Domain a un modelo relacional de EF Core.
        builder.Property<List<Guid>>("_branchIds")
            .HasColumnName("BranchIds")
            .HasConversion(
                branchIds => string.Join(',', branchIds),
                csv => csv.Length == 0
                    ? new List<Guid>()
                    : csv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList(),
                new ValueComparer<List<Guid>>(
                    (a, b) => (a ?? new List<Guid>()).SequenceEqual(b ?? new List<Guid>()),
                    v => v.Aggregate(0, (hash, id) => HashCode.Combine(hash, id)),
                    v => v.ToList()));

        builder.Property(u => u.ConcurrencyToken).IsConcurrencyToken();
    }
}
