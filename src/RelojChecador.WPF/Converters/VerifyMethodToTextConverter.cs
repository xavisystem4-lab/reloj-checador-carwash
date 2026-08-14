using System.Globalization;
using System.Windows.Data;
using RelojChecador.Domain.Attendances;

namespace RelojChecador.WPF.Converters;

/// <summary>Traduce <see cref="AttendanceVerifyMethod"/> a texto legible para la columna
/// "Método" de la pantalla de Asistencia — mismo texto que ya usa el Dashboard web
/// (dashboard/app.js, mapVerifyMethod), para no tener dos traducciones distintas del mismo
/// dato en la misma app.</summary>
public sealed class VerifyMethodToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            AttendanceVerifyMethod method => Describe(method),
            _ => "—",
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string Describe(AttendanceVerifyMethod method) => method switch
    {
        AttendanceVerifyMethod.Fingerprint => "Huella",
        AttendanceVerifyMethod.Password => "Contraseña",
        AttendanceVerifyMethod.Card => "Tarjeta",
        AttendanceVerifyMethod.Face => "Rostro",
        _ => "Desconocido",
    };
}
