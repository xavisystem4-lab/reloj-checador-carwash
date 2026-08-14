using System.Globalization;
using System.Windows;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Diálogo para capturar (o corregir) las deducciones de un empleado en la semana que se
/// está viendo en Reportes — ISR, IMSS y un tercer campo "Otro" de etiqueta libre (para
/// INFONAVIT, préstamos, faltas, etc.). El sistema NUNCA calcula estos montos — el usuario
/// (o su contador) ya los calculó por fuera, ver PayrollDeduction para el porqué.
/// </summary>
public partial class EditPayrollDeductionsDialog : Window
{
    public decimal IsrAmount { get; private set; }
    public decimal ImssAmount { get; private set; }
    public decimal OtherAmount { get; private set; }
    public string? OtherLabel { get; private set; }
    public string? Notes { get; private set; }

    public EditPayrollDeductionsDialog(string employeeName, string weekRangeText, PayrollDeductionValues current)
    {
        InitializeComponent();
        HeaderTextBlock.Text = $"Deducciones de {employeeName} — semana {weekRangeText}";

        IsrTextBox.Text = current.IsrAmount.ToString("0.##", CultureInfo.InvariantCulture);
        ImssTextBox.Text = current.ImssAmount.ToString("0.##", CultureInfo.InvariantCulture);
        OtherAmountTextBox.Text = current.OtherAmount.ToString("0.##", CultureInfo.InvariantCulture);
        OtherLabelTextBox.Text = current.OtherLabel ?? "";
        NotesTextBox.Text = current.Notes ?? "";
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryParseAmount(IsrTextBox.Text, "ISR", out var isr)) return;
        if (!TryParseAmount(ImssTextBox.Text, "IMSS", out var imss)) return;
        if (!TryParseAmount(OtherAmountTextBox.Text, "\"Otro\"", out var other)) return;

        IsrAmount = isr;
        ImssAmount = imss;
        OtherAmount = other;
        OtherLabel = OtherLabelTextBox.Text;
        Notes = NotesTextBox.Text;
        DialogResult = true;
    }

    /// <summary>Un campo vacío se trata como 0 (no capturar nada = no hay deducción de
    /// ese tipo) — mismo criterio de validación (NumberStyles.Number, InvariantCulture,
    /// no-negativo) que WeeklySalary/OvertimeHourlyRate en EditEmployeeDialog.</summary>
    private bool TryParseAmount(string text, string fieldLabel, out decimal amount)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            amount = 0m;
            return true;
        }

        if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) || amount < 0)
        {
            ShowError($"El monto de {fieldLabel} debe ser un número mayor o igual a 0.");
            return false;
        }

        return true;
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }
}
