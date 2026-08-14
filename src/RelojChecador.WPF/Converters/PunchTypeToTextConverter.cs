using System.Globalization;
using System.Windows.Data;

namespace RelojChecador.WPF.Converters;

/// <summary>Traduce el código crudo <c>PunchType</c> (el <c>dwInOutMode</c> que entrega el
/// SDK de ZKTeco) a texto legible para la columna "Tipo" de la tabla de asistencias. La
/// convención 0-5 es la más citada en la documentación/comunidad del SDK — no se ha
/// confirmado cada valor individualmente contra el F22/ID real (solo se vieron 0, 1, 4 y 5
/// en las primeras descargas reales), así que un valor fuera de ese rango se muestra tal
/// cual en vez de forzar una traducción incorrecta.</summary>
public sealed class PunchTypeToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        Describe(value as int?);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>Público para que AttendanceViewModel.BuildCsv use la misma traducción que
    /// ve la pantalla, en vez de duplicarla — el ViewModel no puede depender de
    /// IValueConverter (tipo de WPF), pero sí de este método estático.</summary>
    public static string Describe(int? punchType) => punchType switch
    {
        null => "—",
        0 => "Entrada",
        1 => "Salida",
        2 => "Salida a descanso",
        3 => "Entrada de descanso",
        4 => "Entrada (tiempo extra)",
        5 => "Salida (tiempo extra)",
        _ => $"Desconocido ({punchType})",
    };
}
