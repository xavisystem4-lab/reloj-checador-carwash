using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RelojChecador.Infrastructure.Data;

namespace RelojChecador.Infrastructure.Tests.Data;

/// <summary>
/// Crea un RelojChecadorDbContext contra una base SQLite ":memory:" real (no el
/// proveedor InMemory de EF Core), para que las pruebas de repositorios detecten
/// problemas reales de SQL/tipos/índices. La conexión debe mantenerse abierta durante
/// toda la vida de la fixture porque SQLite ":memory:" desaparece al cerrarla.
/// </summary>
public sealed class SqliteInMemoryFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteInMemoryFixture()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public RelojChecadorDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<RelojChecadorDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new RelojChecadorDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
