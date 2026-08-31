using System.Globalization;

namespace RelojChecador.WPF.Common;

/// <summary>
/// Analiza una hora escrita a mano en cualquier formato razonable — 24 horas ("16:00") o
/// 12 horas con AM/PM ("4:00 PM") — pedido explícito del usuario: "en hora, utilizar, ya
/// sea, cualquier tipo de formato". Usado por todos los diálogos que capturan hora a mano
/// (CreateManualAttendanceDialog, EditAttendanceDialog), para no duplicar la lista de
/// formatos aceptados en cada uno.
/// </summary>
internal static class FlexibleTimeParser
{
    private static readonly string[] Formats =
        ["H:mm", "HH:mm", "h:mm tt", "hh:mm tt", "H:mm:ss", "HH:mm:ss", "h:mm:ss tt", "hh:mm:ss tt"];

    public static bool TryParse(string raw, out TimeSpan value)
    {
        if (TimeOnly.TryParseExact(raw.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            value = parsed.ToTimeSpan();
            return true;
        }

        value = default;
        return false;
    }
}
