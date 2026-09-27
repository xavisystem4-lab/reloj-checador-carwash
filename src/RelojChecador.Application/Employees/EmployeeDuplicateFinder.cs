using System.Linq;
using RelojChecador.Application.Devices;
using RelojChecador.Domain.Employees;

namespace RelojChecador.Application.Employees;

/// <summary>Lo que "Buscar repetidos" necesita saber de cada empleado además del registro:
/// cuántas marcaciones tiene y si tiene PIN en algún reloj — para elegir cuál se queda.</summary>
public sealed record DuplicateCandidate(Employee Employee, int PunchCount, bool HasPin);

/// <summary>Un grupo de registros que parecen la misma persona. <see cref="Keeper"/> es el que
/// se sugiere conservar; <see cref="Others"/> los que se eliminarían (sus marcaciones y datos
/// pasan al que se queda). <see cref="SuggestedName"/> es el nombre más completo del grupo.
/// <see cref="NeedsReview"/>: dentro del grupo hay nombres que NO se parecen entre sí (p. ej.
/// "Jesus Lopez" junto a "Jesus Lopez Garcia" y "Jesus Lopez Ramos", que pueden ser dos
/// personas distintas) — se muestra, pero no se marca solo.</summary>
public sealed record DuplicateGroup(
    DuplicateCandidate Keeper, IReadOnlyList<DuplicateCandidate> Others, string SuggestedName, bool NeedsReview)
{
    public IEnumerable<DuplicateCandidate> All => Others.Prepend(Keeper);
}

/// <summary>
/// "🔍 Buscar repetidos" (pedido explícito del usuario, v1.70.0: "un botón que busque los
/// nombres y apellidos repetidos y con un click eliminarlos").
///
/// Dos registros son "la misma persona" si sus nombres son iguales sin contar mayúsculas,
/// acentos ni espacios, o si TODAS las palabras del más corto aparecen en orden en el más
/// largo ("Adrian Uribe" ↔ "Adrian Uribe Garcia") — el mismo criterio de
/// <see cref="DeviceUserEmployeeMatcher.NamesLookAlike"/>. El nombre corto debe tener al menos
/// dos palabras: "Luis" solo se parecería a medio catálogo.
///
/// Cuál se queda: el que no está dado de baja, luego el que tiene más marcaciones, luego el que
/// tiene PIN, luego el de nombre más completo. Es solo la sugerencia: la pantalla deja cambiarlo.
/// </summary>
public static class EmployeeDuplicateFinder
{
    public static IReadOnlyList<DuplicateGroup> Find(IReadOnlyList<DuplicateCandidate> candidates)
    {
        var n = candidates.Count;
        var words = candidates.Select(c => DeviceUserEmployeeMatcher.NormalizeName(c.Employee.FullName)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToArray();

        var parent = Enumerable.Range(0, n).ToArray();
        int Root(int i) => parent[i] == i ? i : parent[i] = Root(parent[i]);

        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                if (SamePerson(words[i], words[j]))
                {
                    parent[Root(i)] = Root(j);
                }
            }
        }

        var groups = new List<DuplicateGroup>();
        foreach (var members in Enumerable.Range(0, n).GroupBy(Root).Where(g => g.Count() > 1))
        {
            var indexes = members.ToList();
            var needsReview = indexes.Any(a => indexes.Any(b => a < b && !SamePerson(words[a], words[b])));

            var ordered = indexes
                .OrderBy(i => candidates[i].Employee.Status == EmploymentStatus.Terminated)
                .ThenByDescending(i => candidates[i].PunchCount)
                .ThenByDescending(i => candidates[i].HasPin)
                .ThenByDescending(i => words[i].Length)
                .ThenBy(i => candidates[i].Employee.Number.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var keeper = candidates[ordered[0]];
            var suggestedName = needsReview
                ? keeper.Employee.FullName
                : indexes.OrderByDescending(i => words[i].Length).ThenBy(i => i).Select(i => candidates[i].Employee.FullName.Trim()).First();

            groups.Add(new DuplicateGroup(keeper, [.. ordered.Skip(1).Select(i => candidates[i])], suggestedName, needsReview));
        }

        return [.. groups.OrderBy(g => DeviceUserEmployeeMatcher.NormalizeName(g.Keeper.Employee.FullName), StringComparer.Ordinal)];
    }

    private static bool SamePerson(string[] a, string[] b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        if (a.SequenceEqual(b))
        {
            return true;
        }

        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        return shorter.Length >= 2 && IsSubsequence(shorter, longer);
    }

    private static bool IsSubsequence(string[] needle, string[] haystack)
    {
        var next = 0;
        foreach (var word in haystack)
        {
            if (next < needle.Length && needle[next] == word)
            {
                next++;
            }
        }
        return next == needle.Length;
    }
}
