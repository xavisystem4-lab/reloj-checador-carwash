using System.Globalization;
using System.Text;
using RelojChecador.Domain.Employees;

namespace RelojChecador.Application.Devices;

/// <summary>Cómo se resolvió (o por qué no) el vínculo de un usuario del reloj con un
/// empleado — ver <see cref="DeviceUserEmployeeMatcher"/>.</summary>
public enum DeviceUserMatchKind
{
    /// <summary>El nombre del usuario en el reloj coincide con el del empleado (sin
    /// distinguir mayúsculas, acentos ni espacios repetidos).</summary>
    ExactName,

    /// <summary>El reloj guarda el nombre truncado (unos ~24 caracteres): el nombre del
    /// empleado empieza con el del reloj y es el único que lo hace.</summary>
    TruncatedName,

    /// <summary>Sin coincidencia por nombre, pero Employee.Number == PIN — la convención del
    /// catálogo ("se respete tal cual la numeración del PIN").</summary>
    EmployeeNumber,

    /// <summary>Ese PIN ya tiene un vínculo en este dispositivo — no se toca.</summary>
    AlreadyLinked,

    /// <summary>Se encontró al empleado pero ya está vinculado a otro PIN de este
    /// dispositivo (o ya lo reclamó otro usuario del reloj) — un empleado solo puede tener un
    /// PIN por dispositivo.</summary>
    EmployeeAlreadyLinked,

    /// <summary>Varios empleados coinciden — nunca se adivina entre ellos.</summary>
    Ambiguous,

    /// <summary>Ningún empleado coincide — queda para vincular a mano.</summary>
    NoMatch,
}

public sealed record DeviceUserMatchResult(DeviceUserRecord User, Employee? Employee, DeviceUserMatchKind Kind)
{
    /// <summary>true si hay que crear el vínculo PIN↔empleado.</summary>
    public bool ShouldLink =>
        Employee is not null &&
        Kind is DeviceUserMatchKind.ExactName or DeviceUserMatchKind.TruncatedName or DeviceUserMatchKind.EmployeeNumber;
}

/// <summary>
/// Empareja los usuarios que viven en la memoria del reloj (PIN + nombre, ver
/// <see cref="DeviceUserRecord"/>) con los empleados del catálogo local, para crear
/// automáticamente los vínculos (EmployeeDeviceMapping) que faltan. Sin el vínculo, una
/// marcación queda "pendiente de asignación" y el reporte semanal la descarta: el empleado
/// aparece con "Falta" toda la semana aunque sí haya checado.
///
/// Lógica pura, sin acceso a datos ni al dispositivo — quien la llama (DevicesViewModel) es
/// quien lee el reloj y persiste. Criterio deliberadamente conservador: solo vincula cuando
/// hay EXACTAMENTE un empleado candidato; ante duda (varios, o ninguno) no adivina y lo
/// reporta para vincularlo a mano.
/// </summary>
public static class DeviceUserEmployeeMatcher
{
    /// <summary>Largo mínimo del nombre del reloj para aceptar una coincidencia por prefijo —
    /// evita que un nombre corto ("Ana") coincida con cualquier "Ana ..." del catálogo.</summary>
    private const int MinTruncatedNameLength = 15;

    /// <param name="users">Usuarios tal cual están en el reloj.</param>
    /// <param name="employees">Empleados candidatos (los dados de baja se ignoran).</param>
    /// <param name="linkedPins">PINs que YA tienen vínculo en este dispositivo.</param>
    /// <param name="linkedEmployeeIds">Empleados que YA tienen vínculo en este dispositivo.</param>
    public static IReadOnlyList<DeviceUserMatchResult> Match(
        IReadOnlyList<DeviceUserRecord> users,
        IReadOnlyList<Employee> employees,
        IReadOnlySet<string> linkedPins,
        IReadOnlySet<Guid> linkedEmployeeIds)
    {
        var candidates = employees
            .Where(e => e.Status != EmploymentStatus.Terminated)
            .Select(e => (Employee: e, Name: NormalizeName(e.FullName)))
            .ToList();
        var claimed = new HashSet<Guid>(linkedEmployeeIds);

        var results = new List<DeviceUserMatchResult>(users.Count);
        foreach (var user in users)
        {
            var pin = user.DeviceUserPin.Trim();
            if (linkedPins.Contains(pin))
            {
                results.Add(new DeviceUserMatchResult(user, null, DeviceUserMatchKind.AlreadyLinked));
                continue;
            }

            var (employee, kind) = Resolve(user, pin, candidates);
            if (employee is null)
            {
                results.Add(new DeviceUserMatchResult(user, null, kind));
                continue;
            }

            // Un empleado solo puede tener un PIN por dispositivo (índice único) — si ya lo
            // tiene, o ya lo reclamó otro usuario del reloj en esta misma pasada, no se
            // vincula otra vez.
            if (!claimed.Add(employee.Id))
            {
                results.Add(new DeviceUserMatchResult(user, employee, DeviceUserMatchKind.EmployeeAlreadyLinked));
                continue;
            }

            results.Add(new DeviceUserMatchResult(user, employee, kind));
        }

        return results;
    }

    private static (Employee? Employee, DeviceUserMatchKind Kind) Resolve(
        DeviceUserRecord user, string pin, List<(Employee Employee, string Name)> candidates)
    {
        var deviceName = NormalizeName(user.Name);

        if (deviceName.Length > 0)
        {
            var exact = candidates.Where(c => c.Name == deviceName).Select(c => c.Employee).ToList();
            if (exact.Count == 1)
            {
                return (exact[0], DeviceUserMatchKind.ExactName);
            }

            if (exact.Count > 1)
            {
                // Homónimos: el número de empleado = PIN desempata; si no, no se adivina.
                var byNumber = exact.Where(e => NumberMatchesPin(e, pin)).ToList();
                return byNumber.Count == 1
                    ? (byNumber[0], DeviceUserMatchKind.ExactName)
                    : (null, DeviceUserMatchKind.Ambiguous);
            }

            if (deviceName.Length >= MinTruncatedNameLength)
            {
                var truncated = candidates
                    .Where(c => c.Name.Length > deviceName.Length && c.Name.StartsWith(deviceName, StringComparison.Ordinal))
                    .Select(c => c.Employee)
                    .ToList();
                if (truncated.Count == 1)
                {
                    return (truncated[0], DeviceUserMatchKind.TruncatedName);
                }

                if (truncated.Count > 1)
                {
                    return (null, DeviceUserMatchKind.Ambiguous);
                }
            }
        }

        var byPinNumber = candidates.Where(c => NumberMatchesPin(c.Employee, pin)).Select(c => c.Employee).ToList();
        return byPinNumber.Count switch
        {
            1 => (byPinNumber[0], DeviceUserMatchKind.EmployeeNumber),
            > 1 => (null, DeviceUserMatchKind.Ambiguous),
            _ => (null, DeviceUserMatchKind.NoMatch),
        };
    }

    private static bool NumberMatchesPin(Employee employee, string pin) =>
        string.Equals(employee.Number.Value, pin, StringComparison.OrdinalIgnoreCase);

    /// <summary>Minúsculas, sin acentos, solo letras/dígitos y espacios simples — así "José
    /// Pérez", "JOSE  PEREZ" y "jose perez" son el mismo nombre.</summary>
    internal static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        var decomposed = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
