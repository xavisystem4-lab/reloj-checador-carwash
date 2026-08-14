using System.IO;
using System.Windows;
using Microsoft.Win32;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Importación masiva de empleados desde un archivo CSV — a diferencia del resto de
/// diálogos de esta pantalla (que solo capturan datos y dejan que EmployeesView.xaml.cs
/// orqueste la llamada al ViewModel después de cerrarse), este SÍ recibe el
/// <see cref="EmployeesViewModel"/> directamente: es un flujo de varios pasos mientras el
/// diálogo sigue abierto (elegir archivo → ver vista previa con alertas → confirmar) en
/// vez de una sola captura, así que necesita llamarlo dos veces (vista previa, luego
/// importar de verdad) sin cerrarse entre medio.
/// </summary>
public partial class ImportEmployeesDialog : Window
{
    private readonly EmployeesViewModel _viewModel;
    private EmployeesViewModel.EmployeeImportPreview? _preview;

    public ImportEmployeesDialog(EmployeesViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
    }

    private async void OnSelectFileClick(object sender, RoutedEventArgs e)
    {
        var fileDialog = new OpenFileDialog { Filter = "Archivo CSV (*.csv)|*.csv|Todos los archivos (*.*)|*.*" };
        if (fileDialog.ShowDialog() != true)
        {
            return;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(fileDialog.FileName);
        }
        catch (Exception ex)
        {
            ShowErrors([$"No se pudo leer el archivo: {ex.Message}"]);
            return;
        }

        _preview = await _viewModel.PrepareImportPreviewAsync(lines);
        RenderPreview(_preview);
    }

    private void RenderPreview(EmployeesViewModel.EmployeeImportPreview preview)
    {
        SummaryTextBlock.Text =
            $"{preview.TotalRows} fila(s) leída(s) · {preview.ToImport} se importarán · " +
            $"{preview.Duplicates} ya existen (se omiten) · {preview.WithAlerts} con alerta · " +
            $"{preview.WeeklySalaryPending} con sueldo pendiente de captura";
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
            ShowErrors(preview.ParseErrors, prefix: "Líneas con error de formato (no se importarán):\n");
        }
        else
        {
            ErrorsTextBlock.Visibility = Visibility.Collapsed;
        }

        PreviewGrid.ItemsSource = preview.Rows;
        ImportButton.IsEnabled = preview.ToImport > 0;
        ImportButton.Content = preview.ToImport > 0
            ? $"Importar {preview.ToImport} empleado(s) nuevo(s)"
            : "Nada que importar";
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (_preview is null)
        {
            return;
        }

        ImportButton.IsEnabled = false;
        var outcome = await _viewModel.ImportEmployeesAsync(_preview);

        if (!outcome.Success)
        {
            MessageBox.Show(this, outcome.Error, "No se pudo importar", MessageBoxButton.OK, MessageBoxImage.Warning);
            ImportButton.IsEnabled = true;
            return;
        }

        var branchesMessage = outcome.BranchesCreated.Count > 0
            ? $"\n\nSucursales nuevas creadas: {string.Join(", ", outcome.BranchesCreated)}."
            : "";
        MessageBox.Show(
            this,
            $"Se importaron {outcome.Created} empleado(s) nuevo(s).{branchesMessage}\n\n" +
            "Ninguno quedó vinculado a un reloj checador todavía — el archivo no trae esa información. " +
            "Usa \"Vincular a dispositivo\" en la lista para cada uno cuando tengas su PIN.",
            "Importación completada", MessageBoxButton.OK, MessageBoxImage.Information);

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowErrors(IReadOnlyList<string> errors, string prefix = "")
    {
        ErrorsTextBlock.Text = prefix + string.Join("\n", errors);
        ErrorsTextBlock.Visibility = Visibility.Visible;
    }
}
