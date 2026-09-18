using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RelojChecador.Application.Branches;
using RelojChecador.Domain.Branches;
using RelojChecador.Domain.Employees;
using RelojChecador.Infrastructure.Cloud;
using RelojChecador.Infrastructure.Data;

namespace RelojChecador.Infrastructure.Tests.Cloud;

/// <summary>El caso real del 2026-09-18 ("CAFETERIA"): la sucursal local existe con un Id
/// distinto al que Supabase ya tiene para el mismo Code. Se prueba el ciclo completo contra
/// una SQLite real y un Supabase simulado.</summary>
public sealed class SupabaseSyncBranchReconciliationTests : IDisposable
{
    private static readonly Guid RemoteCafeteriaId = Guid.Parse("01a0b52c-7f14-701c-baaf-a6dbb2edc7e7");

    private const string UniqueViolationBody =
        """{"code":"23505","details":"Key (code)=(CAFETERIA) already exists.","hint":null,"message":"duplicate key value violates unique constraint"}""";

    private readonly SqliteConnection _keepAlive;
    private readonly ServiceProvider _provider;
    private readonly FakeHttpMessageHandler _handler = new();
    private readonly SupabaseSyncStatus _status = new();
    private readonly SupabaseSyncBackgroundService _service;

    public SupabaseSyncBranchReconciliationTests()
    {
        var connectionString = $"Data Source=file:sync-{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();

        var options = new SupabaseSyncOptions { Url = "https://fake.supabase.co", ServiceRoleKey = "fake-key" };
        var services = new ServiceCollection();
        services.AddRelojChecadorData(connectionString);
        services.AddSingleton(new SupabaseRestClient(new HttpClient(_handler), options));
        _provider = services.BuildServiceProvider();

        using (var scope = _provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<RelojChecadorDbContext>().Database.EnsureCreated();
        }

        _service = new SupabaseSyncBackgroundService(
            _provider.GetRequiredService<IServiceScopeFactory>(), options, _status,
            NullLogger<SupabaseSyncBackgroundService>.Instance);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _keepAlive.Dispose();
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string RemoteBranchesJson() => $$"""[{"id":"{{RemoteCafeteriaId}}","code":"CAFETERIA"}]""";

    /// <summary>Supabase simulado: ya tiene CAFETERIA con RemoteCafeteriaId, así que un upsert de
    /// sucursales que no traiga ese Id devuelve el 409 real.</summary>
    private void SimulateSupabaseThatAlreadyHasCafeteria()
    {
        _handler.ResponseFactory = request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/branches"))
            {
                return Json(HttpStatusCode.OK, RemoteBranchesJson());
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/branches"))
            {
                var sentBody = _handler.Requests.Last().Body!;
                return sentBody.Contains(RemoteCafeteriaId.ToString(), StringComparison.OrdinalIgnoreCase)
                    ? new HttpResponseMessage(HttpStatusCode.Created)
                    : Json(HttpStatusCode.Conflict, UniqueViolationBody);
            }

            return new HttpResponseMessage(HttpStatusCode.Created);
        };
    }

    [Fact]
    public async Task Ciclo_SucursalLocalConIdDistintoAlDeLaNube_SeReconciliaYSubeTodoEnElMismoCiclo()
    {
        Guid localId;
        using (var scope = _provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RelojChecadorDbContext>();
            var branch = Branch.Create("CAFETERIA", "Cafetería", "America/Tijuana");
            localId = branch.Id;
            context.Branches.Add(branch);
            context.Employees.Add(Employee.Create(
                EmployeeNumber.Create("8001"), "Ana Torres", branch.Id, new DateOnly(2024, 3, 1), weeklySalary: 2500m));
            await context.SaveChangesAsync();
        }

        SimulateSupabaseThatAlreadyHasCafeteria();

        var ok = await _service.TriggerSyncNowAsync();

        Assert.True(ok, _status.LastError);
        Assert.Null(_status.LastError);

        using var readScope = _provider.CreateScope();
        var branches = await readScope.ServiceProvider.GetRequiredService<IBranchRepository>().ListAsync();
        Assert.Equal(RemoteCafeteriaId, Assert.Single(branches).Id);

        // El empleado viaja ya con el Id de la nube: no fallará por llave foránea.
        var employeePost = _handler.Requests.Last(r => r.Method == HttpMethod.Post && r.Uri!.AbsolutePath.EndsWith("/employees"));
        Assert.Contains(RemoteCafeteriaId.ToString(), employeePost.Body!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(localId.ToString(), employeePost.Body!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ciclo_SinConflicto_NoConsultaLaNubeParaReconciliar()
    {
        using (var scope = _provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RelojChecadorDbContext>();
            context.Branches.Add(Branch.Create("NORMAL", "Sucursal normal", "America/Tijuana"));
            await context.SaveChangesAsync();
        }

        var ok = await _service.TriggerSyncNowAsync();

        Assert.True(ok, _status.LastError);
        Assert.DoesNotContain(_handler.Requests, r => r.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task Ciclo_SiElIdDeLaNubeYaExisteEnOtraSucursalLocal_NoTocaNadaYReportaElError()
    {
        // Simula que otra sucursal local ya ocupa el Id que la nube usa para CAFETERIA:
        // reasignar dejaría dos filas con la misma llave, así que debe pedir fusión manual.
        using (var scope = _provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RelojChecadorDbContext>();
            context.Branches.Add(Branch.Create("CAFETERIA", "Cafetería", "America/Tijuana"));
            var squatter = Branch.Create("OTRA", "Otra", "America/Tijuana");
            context.Branches.Add(squatter);
            await context.SaveChangesAsync();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Branches SET Id = {RemoteCafeteriaId} WHERE Id = {squatter.Id}");
        }

        _handler.ResponseFactory = request =>
            request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, RemoteBranchesJson())
                : request.RequestUri!.AbsolutePath.EndsWith("/branches")
                    ? Json(HttpStatusCode.Conflict, UniqueViolationBody)
                    : new HttpResponseMessage(HttpStatusCode.Created);

        var ok = await _service.TriggerSyncNowAsync();

        Assert.False(ok);
        Assert.Contains("branches", _status.LastError);
        using var readScope = _provider.CreateScope();
        var cafeteria = await readScope.ServiceProvider.GetRequiredService<IBranchRepository>().GetByCodeAsync("CAFETERIA");
        Assert.NotEqual(RemoteCafeteriaId, cafeteria!.Id);
    }
}
