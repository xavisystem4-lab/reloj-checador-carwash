using System.Collections;
using System.Windows;

namespace RelojChecador.WPF.Views;

/// <summary>
/// "Ver completo" de las asistencias descargadas — pedido explícito del usuario: la tabla
/// embebida en Dispositivos tiene un alto fijo chico (MaxHeight="180") para no empujar el
/// resto de la pantalla, así que con muchas marcaciones toca scrollear adentro de un espacio
/// diminuto. Esta ventana muestra la MISMA colección (RelojChecador.WPF.ViewModels.RawAttendanceRow,
/// vía DevicesViewModel.AttendanceRecords) sin ese límite, en su propia ventana que se puede
/// maximizar — se sigue actualizando sola en vivo porque es la misma ObservableCollection,
/// no una copia.
/// </summary>
public partial class AttendanceRecordsFullDialog : Window
{
    public AttendanceRecordsFullDialog(IEnumerable records, int count)
    {
        InitializeComponent();
        HeaderTextBlock.Text = $"Asistencias descargadas (esta sesión, aún sin conciliar/persistir) — {count} registro(s)";
        RecordsGrid.ItemsSource = records;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
