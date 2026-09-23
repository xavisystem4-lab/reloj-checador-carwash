using System.Globalization;
using System.Text.RegularExpressions;

namespace RelojChecador.Application.Employees;

/// <summary>Reglas entre el Número de empleado y el PIN del reloj — pedido explícito del
/// usuario: "quiero que los PIN coincidan con el número de Empleado de todos los empleados",
/// con el catálogo en formato "EMP-012" → PIN 12 (solo los dígitos finales, sin ceros a la
/// izquierda).</summary>
public static partial class EmployeeNumberPinRules
{
    private const string DefaultPrefix = "EMP-";

    [GeneratedRegex(@"^\D*(\d+)$")]
    private static partial Regex TrailingDigits();

    [GeneratedRegex(@"^EMP-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PrefixedNumber();

    /// <summary>PIN que le corresponde a un Número: "EMP-012" → "12", "7" → "7". Null si el
    /// número no termina en dígitos ("ABC") o sus dígitos valen 0 (el reloj no acepta PIN 0).</summary>
    public static string? ToDevicePin(string? employeeNumber)
    {
        if (string.IsNullOrWhiteSpace(employeeNumber))
        {
            return null;
        }

        var match = TrailingDigits().Match(employeeNumber.Trim());
        if (!match.Success)
        {
            return null;
        }

        var pin = match.Groups[1].Value.TrimStart('0');
        return pin.Length == 0 ? null : pin;
    }

    /// <summary>Siguiente Número libre con el formato del catálogo actual ("EMP-055" si el
    /// mayor es "EMP-054"). Recibe TODOS los números existentes, dados de baja incluidos —
    /// el índice único de Employee.Number también los cuenta.</summary>
    public static string NextNumber(IEnumerable<string> existingNumbers)
    {
        var max = 0;
        var width = 3;
        foreach (var number in existingNumbers)
        {
            var match = PrefixedNumber().Match(number.Trim());
            if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                max = Math.Max(max, value);
                width = Math.Max(width, match.Groups[1].Value.Length);
            }
        }

        return DefaultPrefix + (max + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
    }
}
