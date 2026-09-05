using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using RelojChecador.Application.Attendances;

namespace RelojChecador.WPF.Converters;

/// <summary>Traduce el semáforo de puntualidad (verde=puntual, amarillo=retardo,
/// rojo=falta — ver <see cref="RelojChecador.Application.Attendances.PunctualityClassifier"/>)
/// a los brushes de tema que YA existen en Colors.Light.xaml/Colors.Dark.xaml
/// (BrushSuccess/BrushWarning/BrushDanger y sus variantes "Surface" de fondo suave) — se
/// reutilizan en vez de definir una paleta nueva, así el color respeta el tema claro/oscuro
/// como el resto de la app. <c>ConverterParameter="Surface"</c> pide el fondo suave de la
/// insignia; sin parámetro pide el color sólido (texto/borde).</summary>
public sealed class AttendanceColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = value is AttendanceColor attendanceColor ? attendanceColor : AttendanceColor.Neutral;
        var wantsSurface = string.Equals(parameter as string, "Surface", StringComparison.OrdinalIgnoreCase);

        var resourceKey = color switch
        {
            AttendanceColor.Green => wantsSurface ? "BrushSuccessSurface" : "BrushSuccess",
            AttendanceColor.Yellow => wantsSurface ? "BrushWarningSurface" : "BrushWarning",
            AttendanceColor.Red => wantsSurface ? "BrushDangerSurface" : "BrushDanger",
            _ => wantsSurface ? "BrushSurfaceAlt" : "BrushTextSecondary",
        };

        return System.Windows.Application.Current.TryFindResource(resourceKey) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
