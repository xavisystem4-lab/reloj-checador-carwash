using System.IO;
using System.Windows;
using Microsoft.Win32;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>
/// "Reemplazar catálogo de empleados" — a diferencia de ImportEmployeesDialog (solo agrega
/// gente nueva, nunca toca a quien ya existe), este trata el archivo como el catálogo
/// COMPLETO: actualiza/crea/da de baja según corresponda (ver
/// EmployeesViewModel.PrepareCatalogReplacePreviewAsync). Mismo patrón de varios pasos que
/// ImportEmployeesDialog — recibe el ViewModel directo porque el diálogo sigue abierto
/// entre "elegir archivo" → "ver vista previa" → "confirmar".
/// </summary>
public partial class ReplaceEmployeeCatalogDialog : Window
{
    private readonly EmployeesViewModel _viewModel;
    private string[]? _csvLines;
    private EmployeesViewModel.EmployeeCatalogReplacePreview? _preview;

    public ReplaceEmployeeCatalogDialog(EmployeesViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
    }

    /// <summary>Genera un CSV de ejemplo con el encabezado exacto que espera este flujo
    /// (10 columnas, distinto del de "Importar desde CSV" — 7 columnas) más una fila de
    /// muestra — mismo pedido que el botón equivalente de ImportEmployeesDialog. La columna
    /// Pin es opcional (déjala vacía si aún no se le asigna reloj a esa persona).</summary>
    private void OnExportTemplateClick(object sender, RoutedEventArgs e)
    {
        var saveDialog = new SaveFileDialog
        {
            FileName = "plantilla_reemplazar_catalogo.csv",
            Filter = "Archivo CSV (*.csv)|*.csv",
        };
        if (saveDialog.ShowDialog() != true)
        {
            return;
        }

        const string template =
            "Number,FullName,Area,Position,HireDate,Status,WeeklySalary,OvertimeHourlyRate,Notes,Pin\r\n" +
            "EMP-001,Nombre Ejemplo,Drive In Car Wash,Puesto ejemplo,2024-01-15,Activo,3500,125,Borra esta fila de ejemplo antes de importar,1\r\n";

        try
        {
            File.WriteAllText(saveDialog.FileName, template);
        }
        catch (Exception ex)
        {
            ShowErrors([$"No se pudo guardar la plantilla: {ex.Message}"]);
        }
    }

    private async void OnSelectFileClick(object sender, RoutedEventArgs e)
    {
        var fileDialog = new OpenFileDialog { Filter = "Archivo CSV (*.csv)|*.csv|Todos los archivos (*.*)|*.*" };
        if (fileDialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _csvLines = File.ReadAllLines(fileDialog.FileName);
        }
        catch (Exception ex)
        {
            _csvLines = null;
            ShowErrors([$"No se pudo leer el archivo: {ex.Message}"]);
            return;
        }

        await RecalculateAsync();
    }

    private async void OnRecalculateClick(object sender, RoutedEventArgs e) => await RecalculateAsync();

    private async Task RecalculateAsync()
    {
        if (_csvLines is null)
        {
            ShowErrors(["Selecciona primero un archivo."]);
            return;
        }

        var protectedNames = ProtectedNamesTextBox.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        _preview = await _viewModel.PrepareCatalogReplacePreviewAsync(_csvLines, protectedNames);
        RenderPreview(_preview);
    }

    private void RenderPreview(EmployeesViewModel.EmployeeCatalogReplacePreview preview)
    {
        SummaryTextBlock.Text =
            $"{preview.TotalRows} fila(s) leída(s) del archivo · {preview.ToCreate} se crearán · " +
            $"{preview.ToUpdate} se actualizarán · {preview.ToRemove.Count} se darán de baja";
        SummaryTextBlock.Visibility = Visibility.Visible;

        if (preview.BranchesToCreate.Count > 0)
        {
            BranchesTextBlock.Text = "Se crearán estas sucursales nuevas: " + string.Join(", ", preview.BranchesToCreate) + ".";
            BranchesTextBlock.Visibility = Visibility.Visible;
        }
        else
        {
            BranchesTextBlock.Visibility = Visibility.Collapsed;
        }

        if (preview.ParseErrors.Count > 0)
        {
            ShowErrors(preview.ParseErrors, prefix: "Líneas con error de formato (no se procesarán):\n");
        }
        else
        {
            ErrorsTextBlock.Visibility = Visibility.Collapsed;
        }

        PreviewGrid.ItemsSource = preview.Rows;
        RemovalTitleTextBlock.Text = $"Se dará de baja ({preview.ToRemove.Count})";
        RemovalGrid.ItemsSource = preview.ToRemove;

        var hasWork = preview.ToCreate > 0 || preview.ToUpdate > 0 || preview.ToRemove.Count > 0;
        ApplyButton.IsEnabled = hasWork;
        ApplyButton.Content = hasWork
            ? $"Aplicar: {preview.ToCreate} crear · {preview.ToUpdate} actualizar · {preview.ToRemove.Count} dar de baja"
            : "Nada que aplicar";
    }

    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (_preview is null)
        {
            return;
        }

        // Confirmación aparte de la vista previa (que ya está en pantalla) — dar de baja a
        // varias decenas de personas de un solo golpe es la acción más delicada de esta
        // pantalla, merece un "¿seguro?" explícito con los números exactos, no solo el
        // texto del botón.
        var confirmed = MessageBox.Show(
            this,
            $"¿Aplicar el reemplazo de catálogo?\n\n" +
            $"• {_preview.ToCreate} empleado(s) nuevo(s)\n" +
            $"• {_preview.ToUpdate} empleado(s) actualizado(s)\n" +
            $"• {_preview.ToRemove.Count} empleado(s) dado(s) de baja\n\n" +
            "Dar de baja es reversible (queda oculto, no se borra — se puede volver a ver marcando " +
            "\"Mostrar dados de baja\"), pero afecta a mucha gente de una sola vez. Revisa la lista de " +
            "\"Se dará de baja\" antes de confirmar.",
            "Confirmar reemplazo de catálogo",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        ApplyButton.IsEnabled = false;
        var outcome = await _viewModel.ApplyCatalogReplaceAsync(_preview);

        if (!outcome.Success)
        {
            MessageBox.Show(this, outcome.Error, "No se pudo aplicar el reemplazo", MessageBoxButton.OK, MessageBoxImage.Warning);
            ApplyButton.IsEnabled = true;
            return;
        }

        var branchesMessage = outcome.BranchesCreated.Count > 0
            ? $"\n\nSucursales nuevas creadas: {string.Join(", ", outcome.BranchesCreated)}."
            : "";
        var pinMessage = outcome.Linked > 0
            ? $"\n\n{outcome.Linked} PIN vinculado(s) a su reloj — usa \"Enviar empleados al reloj\" (en Empleados) para que esa información llegue de verdad al dispositivo físico."
            : "";
        var pinWarningsMessage = outcome.PinWarnings.Count > 0
            ? "\n\n⚠️ PIN no vinculados:\n" + string.Join("\n", outcome.PinWarnings)
            : "";
        MessageBox.Show(
            this,
            $"Listo: {outcome.Created} nuevo(s), {outcome.Updated} actualizado(s), {outcome.Removed} dado(s) de baja.{branchesMessage}{pinMessage}{pinWarningsMessage}",
            "Reemplazo completado", MessageBoxButton.OK, MessageBoxImage.Information);

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowErrors(IReadOnlyList<string> errors, string prefix = "")
    {
        ErrorsTextBlock.Text = prefix + string.Join("\n", errors);
        ErrorsTextBlock.Visibility = Visibility.Visible;
    }
}
