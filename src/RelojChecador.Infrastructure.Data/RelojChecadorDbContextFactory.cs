using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RelojChecador.Infrastructure.Data;

/// <summary>
/// Fábrica usada únicamente por las herramientas de diseño de EF Core
/// (`dotnet ef migrations add`, `dotnet ef database update`) para poder construir el
/// DbContext sin necesitar el host completo de la aplicación. No se usa en tiempo de
/// ejecución — ver <see cref="DependencyInjection.AddRelojChecadorData"/> para eso.
/// </summary>
public sealed class RelojChecadorDbContextFactory : IDesignTimeDbContextFactory<RelojChecadorDbContext>
{
    public RelojChecadorDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<RelojChecadorDbContext>();
        optionsBuilder.UseSqlite("Data Source=relojchecador.design.db");
        return new RelojChecadorDbContext(optionsBuilder.Options);
    }
}
