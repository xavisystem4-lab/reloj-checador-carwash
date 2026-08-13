using CommunityToolkit.Mvvm.ComponentModel;
using RelojChecador.Application.Branches;
using Serilog;

namespace RelojChecador.WPF.ViewModels;

/// <summary>
/// ViewModel de la ventana principal. Por ahora solo demuestra que el pipeline completo
/// funciona de verdad — DI → EF Core → SQLite local → binding en pantalla — mientras se
/// construye la navegación completa (Fase 3) sobre esta base. No es la pantalla final.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IBranchRepository _branchRepository;

    [ObservableProperty]
    private string _statusMessage = "Cargando información local...";

    [ObservableProperty]
    private bool _isLoading = true;

    public MainViewModel(IBranchRepository branchRepository)
    {
        _branchRepository = branchRepository;
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            var branches = await _branchRepository.ListAsync();
            StatusMessage = branches.Count == 0
                ? "Aún no hay sucursales registradas."
                : $"{branches.Count} sucursal(es) registrada(s) en la base local.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo cargar la lista de sucursales al iniciar la ventana principal.");
            // El mensaje en pantalla es comprensible para el usuario, sin tecnicismos —
            // el detalle completo de la excepción queda en el log, no en la UI.
            StatusMessage = "No se pudo cargar la información local. Revisa el registro de errores.";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
