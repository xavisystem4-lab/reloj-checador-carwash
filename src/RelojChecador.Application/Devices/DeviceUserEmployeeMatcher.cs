using System.Globalization;
using System.Text;
using RelojChecador.Domain.Employees;

namespace RelojChecador.Application.Devices;

/// <summary>Cómo se resolvió (o por qué no) el vínculo de un PIN del reloj con un empleado —
/// ver <see cref="DeviceUserEmployeeMatcher"/>.</summary>
public enum PinMatchKind
{
    /// <summary>Todas las palabras del nombre del empleado aparecen, en orden, en alguno de
    /// los nombres que ese PIN tiene hoy (el del reloj o el del empleado dado de baja que lo
    /// tenía) — p. ej. "Antony Beltran" ↔ "Antony Salvador Beltran Garcia".</summary>
    NameMatch,

    /// <summary>Sin coincidencia por nombre, pero Employee.Number == PIN (solo dígitos, sin
    /// distinguir ceros a la izquierda). Solo es respaldo: en un catálogo renumerado
    /// ("EMP-007") el número YA NO coincide con el PIN y no se usa como criterio principal.</summary>
    EmployeeNumber,

    /// <summary>Ese PIN ya pertenece a un empleado vigente (no dado de baja) — no se toca.</summary>
    AlreadyLinked,

    /// <summary>Hay más de un empleado que podría ser — nunca se adivina.</summary>
    Ambiguous,

    /// <summary>Ningún empleado vigente sin PIN coincide — queda para vincular a mano.</summary>
    NoMatch,
}

/// <summary>Un PIN del reloj tal como está hoy. <paramref name="Names"/> son todos los nombres
/// con que se puede reconocer a su dueño: el que tiene en la memoria del reloj y/o el del
/// empleado (posiblemente dado de baja) al que hoy está vinculado. <paramref name="MappedEmployeeId"/>
/// es ese empleado, o null si el PIN no tiene vínculo.</summary>
public sealed record PinSlot(string Pin, IReadOnlyList<string> Names, Guid? MappedEmployeeId)
{
    public string DisplayName => Names.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "(sin nombre)";
}

public sealed record PinMatchResult(PinSlot Slot, Employee? Employee, PinMatchKind Kind)
{
    /// <summary>true si hay que asignar este PIN a <see cref="Employee"/> (creando el vínculo,
    /// o traspasando el que tenía un empleado dado de baja).</summary>
    public bool ShouldLink => Employee is not null && Kind is PinMatchKind.NameMatch or PinMatchKind.EmployeeNumber;
}

/// <summary>
/// Empareja los PINs del reloj con los empleados VIGENTES del catálogo, para crear los
/// vínculos (EmployeeDeviceMapping) que faltan. Sin vínculo a un empleado vigente, el reporte
/// semanal descarta las marcaciones y el empleado sale con "Falta" aunque sí haya checado.
///
/// Caso real que motivó este diseño (18/09/2026): "Reemplazar catálogo" creó 54 empleados
/// nuevos (EMP-001…EMP-054, nombres cortos como "Adali") y dio de baja a los anteriores
/// (número = PIN, nombres completos como "Adali Monserrat Tabanico Ramos", PIN 38), que
/// siguieron siendo dueños de su PIN y de todas sus marcaciones. El número nuevo no coincide
/// con el PIN (Andrés Herrera es EMP-007 pero su PIN es 6), así que se empareja por NOMBRE.
///
/// Lógica pura, sin acceso a datos ni al dispositivo. Criterio conservador: un PIN solo se
/// asigna cuando hay UN candidato claro; ante duda no adivina y lo reporta.
/// </summary>
public static class DeviceUserEmployeeMatcher
{
    /// <param name="slots">Todos los PINs del dispositivo (los del reloj y los que ya tienen vínculo).</param>
    /// <param name="employees">TODOS los empleados, incluidos los dados de baja (se necesitan para
    /// saber si el dueño actual de un PIN sigue vigente).</param>
    public static IReadOnlyList<PinMatchResult> Match(IReadOnlyList<PinSlot> slots, IReadOnlyList<Employee> employees)
    {
        var active = employees.Where(e => e.Status != EmploymentStatus.Terminated).ToList();
        var activeIds = active.Select(e => e.Id).ToHashSet();

        var results = new PinMatchResult?[slots.Count];
        var employeesWithPin = new HashSet<Guid>();
        for (var i = 0; i < slots.Count; i++)
        {
            if (slots[i].MappedEmployeeId is { } owner && activeIds.Contains(owner))
            {
                results[i] = new PinMatchResult(slots[i], null, PinMatchKind.AlreadyLinked);
                employeesWithPin.Add(owner);
            }
        }

        var pool = active
            .Where(e => !employeesWithPin.Contains(e.Id))
            .Select(e => (Employee: e, Tokens: Tokenize(e.FullName)))
            .Where(e => e.Tokens.Count > 0)
            .OrderByDescending(e => e.Tokens.Count)
            .ThenBy(e => e.Employee.Number.Value, StringComparer.Ordinal)
            .ToList();
        var slotNames = slots.Select(s => s.Names.Select(Tokenize).Where(t => t.Count > 0).ToList()).ToList();

        // 1) Por nombre, en pasadas: cada empleado sin pareja mira qué PINs libres lo
        // contienen; solo se asigna si tiene UN solo PIN posible y, entre todos los que
        // compiten por ese PIN, es el más específico (más palabras) sin empate. Repetir hasta
        // que no cambie nada resuelve casos encadenados ("Pablo" cabe en dos PINs hasta que
        // "Jose Pablo Rojo" se queda con el suyo).
        var assigned = new Dictionary<int, (Employee Employee, PinMatchKind Kind)>();
        var used = new HashSet<Guid>();
        bool changed;
        do
        {
            changed = false;
            foreach (var (employee, tokens) in pool)
            {
                if (used.Contains(employee.Id))
                {
                    continue;
                }

                var candidates = CandidateSlots(tokens, slotNames, results, assigned);
                if (candidates.Count != 1)
                {
                    continue;
                }

                var slotIndex = candidates[0];
                var best = pool
                    .Where(p => !used.Contains(p.Employee.Id) && CandidateSlots(p.Tokens, slotNames, results, assigned).Contains(slotIndex))
                    .GroupBy(p => p.Tokens.Count)
                    .OrderByDescending(g => g.Key)
                    .First()
                    .ToList();
                if (best.Count == 1 && best[0].Employee.Id == employee.Id)
                {
                    assigned[slotIndex] = (employee, PinMatchKind.NameMatch);
                    used.Add(employee.Id);
                    changed = true;
                }
            }
        }
        while (changed);

        // 2) Respaldo: Employee.Number == PIN, solo con un único empleado libre.
        for (var i = 0; i < slots.Count; i++)
        {
            if (results[i] is not null || assigned.ContainsKey(i))
            {
                continue;
            }

            var byNumber = pool.Where(p => !used.Contains(p.Employee.Id) && NumberMatchesPin(p.Employee, slots[i].Pin)).ToList();
            if (byNumber.Count == 1)
            {
                assigned[i] = (byNumber[0].Employee, PinMatchKind.EmployeeNumber);
                used.Add(byNumber[0].Employee.Id);
            }
        }

        for (var i = 0; i < slots.Count; i++)
        {
            if (results[i] is not null)
            {
                continue;
            }

            if (assigned.TryGetValue(i, out var match))
            {
                results[i] = new PinMatchResult(slots[i], match.Employee, match.Kind);
                continue;
            }

            var stillPossible = pool.Any(p => !used.Contains(p.Employee.Id) && ContainsTokens(slotNames[i], p.Tokens));
            results[i] = new PinMatchResult(slots[i], null, stillPossible ? PinMatchKind.Ambiguous : PinMatchKind.NoMatch);
        }

        return results!;
    }

    private static List<int> CandidateSlots(
        IReadOnlyList<string> employeeTokens, List<List<IReadOnlyList<string>>> slotNames,
        PinMatchResult?[] fixedResults, Dictionary<int, (Employee Employee, PinMatchKind Kind)> assigned)
    {
        var candidates = new List<int>();
        for (var i = 0; i < slotNames.Count; i++)
        {
            if (fixedResults[i] is null && !assigned.ContainsKey(i) && ContainsTokens(slotNames[i], employeeTokens))
            {
                candidates.Add(i);
            }
        }

        return candidates;
    }

    /// <summary>¿Alguno de los nombres contiene TODAS las palabras del empleado, en el mismo orden?</summary>
    private static bool ContainsTokens(List<IReadOnlyList<string>> names, IReadOnlyList<string> employeeTokens) =>
        names.Any(name => IsSubsequence(employeeTokens, name));

    private static bool IsSubsequence(IReadOnlyList<string> needle, IReadOnlyList<string> haystack)
    {
        var next = 0;
        foreach (var word in haystack)
        {
            if (next < needle.Count && needle[next] == word)
            {
                next++;
            }
        }

        return next == needle.Count;
    }

    /// <summary>Igual sin distinguir mayúsculas; y si ambos son solo dígitos, también sin
    /// ceros a la izquierda ("0114" = "114").</summary>
    private static bool NumberMatchesPin(Employee employee, string pin)
    {
        var number = employee.Number.Value.Trim();
        var trimmedPin = pin.Trim();
        if (string.Equals(number, trimmedPin, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return number.Length > 0 && trimmedPin.Length > 0 && number.All(char.IsAsciiDigit) && trimmedPin.All(char.IsAsciiDigit)
            && number.TrimStart('0') == trimmedPin.TrimStart('0');
    }

    private static IReadOnlyList<string> Tokenize(string? name) =>
        NormalizeName(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);

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
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
