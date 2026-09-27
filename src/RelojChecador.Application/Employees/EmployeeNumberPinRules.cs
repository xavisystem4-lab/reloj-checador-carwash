using System.Text.RegularExpressions;

namespace RelojChecador.Application.Employees;

/// <summary>Reglas entre el Número de empleado y el PIN del reloj — pedido explícito del
/// usuario: "quiero que los PIN coincidan con el número de Empleado de todos los empleados",
/// con el catálogo en formato "EMP-012" → PIN 12 (solo los dígitos finales, sin ceros a la
/// izquierda).</summary>
public static partial class EmployeeNumberPinRules
{
    [GeneratedRegex(@"^\D*(\d+)$")]
    private static partial Regex TrailingDigits();

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
}
