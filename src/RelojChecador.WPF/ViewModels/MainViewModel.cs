using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using RelojChecador.Application.Branches;
using RelojChecador.Application.Common;
using RelojChecador.Domain.Branches;
using RelojChecador.Domain.Common;
using Serilog;

namespace RelojChecador.WPF.ViewModels;

/// <summary>
/// ViewModel de la ventana principal: lista y permite crear sucursales. Es la primera
/// pantalla funcional real (Fase 4) — la navegación completa con las demás secciones
/// (Empleados, Dispositivos, Asistencia, etc. — Fase 3 del diseño visual) todavía no
/// existe; esta es la base sobre la que se construyen las siguientes.
///
/// No conoce tipos de WPF (Window, Dialog): quien la usa (MainWindow, en el code-behind)
/// es responsable de mostrar el diálogo y le pasa los datos capturados ya como texto.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IBranchRepository _branchRepository;
    private readonly IUnitOfWork _unitOfWork;

    [ObservableProperty]
    private string _statusMessage = "Cargando información local...";

    [ObservableProperty]
    private bool _isLoading = true;

    public ObservableCollection<Branch> Branches { get; } = [];

    public MainViewModel(IBranchRepository branchRepository, IUnitOfWork unitOfWork)
    {
        _branchRepository = branchRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            var branches = await _branchRepository.ListAsync();
            Branches.Clear();
            foreach (var branch in branches)
            {
                Branches.Add(branch);
            }

            RefreshStatusMessage();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo cargar la lista de sucursales al iniciar la ventana principal.");
            StatusMessage = "No se pudo cargar la información local. Revisa el registro de errores.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <returns>Un mensaje de error comprensible si algo salió mal, o null si se guardó correctamente.</returns>
    public async Task<string?> CreateBranchAsync(
        string code, string name, string timeZoneId, string? legalEntityName, string? address)
    {
        try
        {
            var branch = Branch.Create(code, name, timeZoneId, legalEntityName, address);
            await _branchRepository.AddAsync(branch);
            await _unitOfWork.SaveChangesAsync();

            Branches.Add(branch);
            RefreshStatusMessage();
            return null;
        }
        catch (DomainException ex)
        {
            // Ej.: código/nombre vacíos — validación de negocio, mensaje ya comprensible.
            return ex.Message;
        }
        catch (DbUpdateException ex)
        {
            // El índice único de Branch.Code (ver EfBranchRepository/BranchConfiguration) es
            // lo que normalmente dispara esto — se interpreta como duplicado sin inspeccionar
            // el texto del error nativo de SQLite, que no es estable entre versiones.
            Log.Warning(ex, "No se pudo guardar la sucursal, probablemente por código duplicado (Code={Code})", code);
            return "Ya existe una sucursal con ese código.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al crear una sucursal (Code={Code})", code);
            return "Ocurrió un error inesperado al guardar. Revisa el registro de errores.";
        }
    }

    private void RefreshStatusMessage()
    {
        StatusMessage = Branches.Count == 0
            ? "Aún no hay sucursales registradas."
            : $"{Branches.Count} sucursal(es) registrada(s) en la base local.";
    }
}
