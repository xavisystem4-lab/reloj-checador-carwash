using System.Text.Json.Nodes;
using RelojChecador.Infrastructure.Cloud;

namespace RelojChecador.Infrastructure.Tests.Cloud;

public class SupabaseLocalConfigStoreTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "RelojChecadorTests_" + Guid.NewGuid());

    private string FilePath => Path.Combine(_tempDirectory, "appsettings.Local.json");

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveServiceRoleKeyAsync_SinArchivoPrevio_CreaElArchivoYLoAplicaEnMemoria()
    {
        var store = new SupabaseLocalConfigStore(FilePath);
        var options = new SupabaseSyncOptions { Url = "https://ejemplo.supabase.co" };

        await store.SaveServiceRoleKeyAsync("clave-secreta", options);

        Assert.True(File.Exists(FilePath));
        Assert.Equal("clave-secreta", options.ServiceRoleKey);
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public async Task SaveServiceRoleKeyAsync_ConEspaciosAlrededor_LosRecorta()
    {
        var store = new SupabaseLocalConfigStore(FilePath);
        var options = new SupabaseSyncOptions();

        await store.SaveServiceRoleKeyAsync("  clave-con-espacios  ", options);

        Assert.Equal("clave-con-espacios", options.ServiceRoleKey);
    }

    [Fact]
    public async Task SaveServiceRoleKeyAsync_ConClaveVacia_LanzaExcepcionYNoEscribeNada()
    {
        var store = new SupabaseLocalConfigStore(FilePath);
        var options = new SupabaseSyncOptions();

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveServiceRoleKeyAsync("   ", options));
        Assert.False(File.Exists(FilePath));
        Assert.Null(options.ServiceRoleKey);
    }

    [Fact]
    public async Task SaveServiceRoleKeyAsync_ConArchivoExistente_PreservaOtrasClavesDelJson()
    {
        Directory.CreateDirectory(_tempDirectory);
        await File.WriteAllTextAsync(FilePath, """{"Supabase":{"OtraCosa":"no tocar"},"OtraSeccion":{"X":1}}""");
        var store = new SupabaseLocalConfigStore(FilePath);
        var options = new SupabaseSyncOptions();

        await store.SaveServiceRoleKeyAsync("clave-nueva", options);

        var written = JsonNode.Parse(await File.ReadAllTextAsync(FilePath))!.AsObject();
        Assert.Equal("clave-nueva", written["Supabase"]!["ServiceRoleKey"]!.GetValue<string>());
        Assert.Equal("no tocar", written["Supabase"]!["OtraCosa"]!.GetValue<string>());
        Assert.Equal(1, written["OtraSeccion"]!["X"]!.GetValue<int>());
    }

    [Fact]
    public async Task SaveServiceRoleKeyAsync_ConArchivoCorrupto_NoRevientaYGuardaIgual()
    {
        Directory.CreateDirectory(_tempDirectory);
        await File.WriteAllTextAsync(FilePath, "{ esto no es json valido");
        var store = new SupabaseLocalConfigStore(FilePath);
        var options = new SupabaseSyncOptions();

        await store.SaveServiceRoleKeyAsync("clave-nueva", options);

        Assert.Equal("clave-nueva", options.ServiceRoleKey);
    }
}
