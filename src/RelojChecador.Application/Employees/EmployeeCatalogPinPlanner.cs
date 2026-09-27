using RelojChecador.Application.Devices;
using RelojChecador.Domain.Employees;

namespace RelojChecador.Application.Employees;

/// <summary>Una fila del catálogo que trae PIN — <paramref name="Index"/> es su posición en el archivo.</summary>
public sealed record CatalogPinRow(int Index, string Number, string FullName, string Pin);

public enum CatalogPinResolution
{
    /// <summary>Se actualiza un empleado vigente que ya existe.</summary>
    Existing,

    /// <summary>Se reactiva un empleado dado de baja (el dueño de ese PIN, o el de ese nombre).</summary>
    Reactivate,

    /// <summary>Nadie en el sistema corresponde: se da de alta.</summary>
    Create,

    /// <summary>Otra fila del archivo ya es esta misma persona (típico al leer del reloj: un PIN
    /// "de relleno" además del real) — esta se omite.</summary>
    SkipDuplicate,
}

/// <param name="Survivor">El empleado que queda con esta fila (null si se crea o se omite).</param>
/// <param name="Absorbed">Registros viejos DADOS DE BAJA de esta misma persona: sus marcaciones y
/// los datos que le falten al que queda pasan a <paramref name="Survivor"/>.</param>
/// <param name="DuplicateOfIndex">Con <see cref="CatalogPinResolution.SkipDuplicate"/>, la fila que sí se usa.</param>
public sealed record CatalogPinPlan(
    CatalogPinRow Row, CatalogPinResolution Resolution, Employee? Survivor, IReadOnlyList<Employee> Absorbed, int? DuplicateOfIndex);

/// <summary>
/// Decide a qué empleado corresponde cada fila de un catálogo que trae PIN — pedido explícito del
/// usuario al subir la lista "ID Empleado, Nombre completo" del reloj. El PIN del reloj es la
/// identidad: una fila se resuelve primero por quién ya checa con ese PIN, luego por nombre
/// (<see cref="DeviceUserEmployeeMatcher"/>: "Antony Beltran" ↔ "Antony Salvador Beltran Garcia").
///
/// CAUSA REAL (Supabase, 27/09/2026): cada persona tenía DOS registros — el vigente
/// ("EMP-001 · Adrian Uribe", con el PIN, las checadas y el sueldo) y uno viejo dado de baja
/// ("1 · Adrian Uribe Garcia", con el horario y la fecha de ingreso). Emparejar solo por nombre
/// exacto reactivaba los viejos y daba de baja a los vigentes, dejando el reporte vacío. Aquí el
/// vigente se queda y el viejo se ABSORBE en él.
/// </summary>
public static class EmployeeCatalogPinPlanner
{
    /// <param name="ownerByPin">Dueño de cada PIN en el reloj, según los vínculos del sistema.</param>
    /// <param name="pinsWithPunches">PINs con al menos una marcación en ese reloj.</param>
    public static IReadOnlyList<CatalogPinPlan> Plan(
        IReadOnlyList<CatalogPinRow> rows, IReadOnlyList<Employee> employees,
        IReadOnlyDictionary<string, Guid> ownerByPin, IReadOnlySet<string> pinsWithPunches)
    {
        var employeesById = employees.ToDictionary(e => e.Id);
        Employee? OwnerOf(string pin) =>
            ownerByPin.TryGetValue(pin, out var id) && employeesById.TryGetValue(id, out var owner) ? owner : null;

        var slots = rows.Select(r =>
        {
            var owner = OwnerOf(r.Pin);
            IReadOnlyList<string> names = owner is null ? [r.FullName] : [r.FullName, owner.FullName];
            return new PinSlot(r.Pin, names, owner?.Id, pinsWithPunches.Contains(r.Pin));
        }).ToList();
        var matches = DeviceUserEmployeeMatcher.Match(slots, employees);

        // 1) Quién queda con cada fila.
        var resolved = new (CatalogPinResolution Resolution, Employee? Survivor)[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var match = matches[i];
            var owner = OwnerOf(row.Pin);

            // Released: PIN "de relleno" cuyo dueño se emparejó con su PIN real en otra fila — se
            // deja a nombre del dueño para que el paso 2 lo marque como duplicado de esa fila.
            if (match.Kind is PinMatchKind.AlreadyLinked or PinMatchKind.Released && owner is not null)
            {
                resolved[i] = (CatalogPinResolution.Existing, owner);
            }
            else if (match.ShouldLink)
            {
                resolved[i] = (CatalogPinResolution.Existing, match.Employee);
            }
            else if (owner is { Status: EmploymentStatus.Terminated })
            {
                // Nadie vigente: se confía en el PIN — quien lo tiene en el reloj es esta persona,
                // aunque su nombre viejo tenga otra ortografía ("Cristoper" / "Cristopher").
                resolved[i] = (CatalogPinResolution.Reactivate, owner);
            }
            else if (FindTerminatedByName(row, employees) is { } byName)
            {
                resolved[i] = (CatalogPinResolution.Reactivate, byName);
            }
            else
            {
                resolved[i] = (CatalogPinResolution.Create, null);
            }
        }

        // 2) Dos filas no pueden quedarse con la misma persona: se usa la que tiene checadas
        // (o el PIN más bajo) y la otra se omite.
        var keptIndexBySurvivor = new Dictionary<Guid, int>();
        foreach (var i in Enumerable.Range(0, rows.Count)
                     .Where(i => resolved[i].Survivor is not null)
                     .OrderByDescending(i => pinsWithPunches.Contains(rows[i].Pin))
                     .ThenBy(i => PinSortKey(rows[i].Pin)))
        {
            keptIndexBySurvivor.TryAdd(resolved[i].Survivor!.Id, i);
        }

        var survivorIds = keptIndexBySurvivor.Keys.ToHashSet();
        var absorbedIds = new HashSet<Guid>();
        var plans = new CatalogPinPlan[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            var (resolution, survivor) = resolved[i];
            if (survivor is not null && keptIndexBySurvivor[survivor.Id] != i)
            {
                plans[i] = new CatalogPinPlan(rows[i], CatalogPinResolution.SkipDuplicate, null, [], keptIndexBySurvivor[survivor.Id]);
                continue;
            }

            // 3) Registros viejos (dados de baja) de esta misma persona: el dueño viejo del PIN, el
            // del mismo nombre exacto, o quien tenga su número con un nombre parecido.
            var row = rows[i];
            var normalizedName = DeviceUserEmployeeMatcher.NormalizeName(row.FullName);
            var absorbed = employees
                .Where(e => e.Status == EmploymentStatus.Terminated
                            && e.Id != survivor?.Id
                            && !survivorIds.Contains(e.Id)
                            && !absorbedIds.Contains(e.Id)
                            && (e.Id == OwnerOf(row.Pin)?.Id
                                || DeviceUserEmployeeMatcher.NormalizeName(e.FullName) == normalizedName
                                || (string.Equals(e.Number.Value, row.Number, StringComparison.OrdinalIgnoreCase)
                                    && DeviceUserEmployeeMatcher.NamesLookAlike(e.FullName, row.FullName))))
                .ToList();
            absorbedIds.UnionWith(absorbed.Select(e => e.Id));

            plans[i] = new CatalogPinPlan(row, resolution, survivor, absorbed, null);
        }

        return plans;
    }

    /// <summary>Un dado de baja con el mismo nombre (exacto primero; si no, parecido y con ese
    /// número) — solo si hay exactamente uno.</summary>
    private static Employee? FindTerminatedByName(CatalogPinRow row, IReadOnlyList<Employee> employees)
    {
        var terminated = employees.Where(e => e.Status == EmploymentStatus.Terminated).ToList();
        var normalizedName = DeviceUserEmployeeMatcher.NormalizeName(row.FullName);

        var exact = terminated.Where(e => DeviceUserEmployeeMatcher.NormalizeName(e.FullName) == normalizedName).ToList();
        if (exact.Count > 1)
        {
            exact = exact.Where(e => string.Equals(e.Number.Value, row.Number, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (exact.Count == 1)
        {
            return exact[0];
        }

        var byNumber = terminated
            .Where(e => string.Equals(e.Number.Value, row.Number, StringComparison.OrdinalIgnoreCase)
                        && DeviceUserEmployeeMatcher.NamesLookAlike(e.FullName, row.FullName))
            .ToList();
        return byNumber.Count == 1 ? byNumber[0] : null;
    }

    private static int PinSortKey(string pin) => int.TryParse(pin, out var n) ? n : int.MaxValue;
}
