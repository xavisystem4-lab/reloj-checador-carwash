using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RelojChecador.WPF.Converters;

/// <summary>Oculta el panel de detalle mientras no haya un elemento seleccionado
/// (p. ej. la pantalla de Dispositivos antes de elegir uno de la lista).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
