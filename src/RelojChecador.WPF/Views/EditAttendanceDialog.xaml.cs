using System.Globalization;
using System.Windows;
using RelojChecador.WPF.Common;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Edita PunchType, Notes y la FECHA/HORA de UNA marcación ya existente, o la borra por
/// completo — pedido explícito del usuario: "que las asistencias se puedan editar ... y
/// pueda colocarle si es entrada o salida ... nota en especial también. o eliminar
/// Marcación", y después: "también podamos editar la hora ... el empleado llegó temprano,
/// pero checó hasta ahorita ... necesitamos colocar que a la hora en que llegó, que no
/// afecte". El guardado/borrado real vive en
/// AttendanceViewModel.EditAttendanceAsync/DeleteAttendanceAsync, no aquí — este diálogo
/// solo captura la intención del usuario.
///
/// <see cref="DeleteRequested"/> distingue las dos salidas posibles con
/// <c>DialogResult == true</c>: "Guardar" (edición) vs "Eliminar marcación" (borrado, con su
/// propia confirmación aparte antes de cerrar el diálogo).
/// </summary>
public partial class EditAttendanceDialog : Window
{
    public bool DeleteRequested { get; private set; }
    public int? PunchType => EntradaRadioButton.IsChecked == true ? 0 : SalidaRadioButton.IsChecked == true ? 1 : null;
    public string? Notes => string.IsNullOrWhiteSpace(NotesTextBox.Text) ? null : NotesTextBox.Text.Trim();
    public DateTime? Timestamp { get; private set; }

    public EditAttendanceDialog(AttendanceRow row)
    {
        InitializeComponent();

        EmployeeTextBlock.Text = row.EmployeeDisplay;

        // TimestampUtc pese al nombre es, en la práctica, la hora LOCAL del dispositivo
        // (ver comentario de clase de ShiftPunchTypeClassifier) — se precarga tal cual,
        // sin ninguna conversión de huso horario, mismo criterio que el resto de la app.
        var currentTimestamp = row.Attendance.TimestampUtc;
        DatePickerControl.SelectedDate = currentTimestamp.Date;
        TimeTextBox.Text = currentTimestamp.ToString("HH:mm", CultureInfo.InvariantCulture);

        if (row.Attendance.PunchType == 1)
        {
            SalidaRadioButton.IsChecked = true;
        }
        else
        {
            // Cualquier otro valor (0, null, o un código crudo viejo del dispositivo como
            // 2-5) cae en Entrada por defecto — mismo criterio conservador que el resto de
            // la clasificación (ver ShiftPunchTypeClassifier).
            EntradaRadioButton.IsChecked = true;
        }

        NotesTextBox.Text = row.Attendance.Notes ?? "";
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (PunchType is null)
        {
            ErrorTextBlock.Text = "Elige un tipo (Entrada o Salida).";
            ErrorTextBlock.Visibility = Visibility.Visible;
            return;
        }

        if (DatePickerControl.SelectedDate is not { } date)
        {
            ErrorTextBlock.Text = "La fecha es obligatoria.";
            ErrorTextBlock.Visibility = Visibility.Visible;
            return;
        }

        if (!FlexibleTimeParser.TryParse(TimeTextBox.Text, out var time))
        {
            ErrorTextBlock.Text = "La hora no es válida (cualquier formato, ej.: 08:00 o 8:00 AM).";
            ErrorTextBlock.Visibility = Visibility.Visible;
            return;
        }

        Timestamp = date.Date.Add(time);
        DialogResult = true;
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var confirmed = MessageBox.Show(
            this,
            $"¿Borrar PERMANENTEMENTE esta marcación ({EmployeeTextBlock.Text})?\n\n" +
            "Esto NO se puede deshacer. Además, el borrado no se refleja en el Dashboard/nube " +
            "(la sincronización solo sube cambios, nunca borra ahí) — si ya se había subido, " +
            "hay que borrarla aparte desde Supabase.",
            "Confirmar borrado permanente",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        DeleteRequested = true;
        DialogResult = true;
    }
}
