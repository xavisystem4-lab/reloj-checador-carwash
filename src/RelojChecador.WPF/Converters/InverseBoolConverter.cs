using System.Globalization;
using System.Windows.Data;

namespace RelojChecador.WPF.Converters;

/// <summary>Invierte un bool — usado para deshabilitar un botón mientras su propio comando
/// está en curso (p. ej. "Conectar con nube" mientras IsCloudSyncBusy es true).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool boolValue && !boolValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool boolValue && !boolValue;
}
