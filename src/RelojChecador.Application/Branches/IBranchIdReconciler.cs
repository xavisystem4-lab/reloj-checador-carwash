namespace RelojChecador.Application.Branches;

public enum BranchIdReassignResult
{
    /// <summary>El Id de la sucursal (y el de todo lo que la referenciaba) ya se cambió.</summary>
    Reassigned,

    /// <summary>Ya no existe una sucursal local con el Id actual — nada que hacer.</summary>
    SourceNotFound,

    /// <summary>Ya existe OTRA sucursal local con el Id destino: cambiarlo dejaría dos filas
    /// con la misma llave. No se toca nada; requiere una fusión manual.</summary>
    TargetAlreadyExists,
}

/// <summary>
/// Cambia el Id de una sucursal local, junto con todo lo que la referencia, para que coincida
/// con el Id que esa misma sucursal (mismo Code) ya tiene en Supabase. Existe porque el
/// motor de sincronización es push-only y nunca manda DELETE: si una sucursal se borra
/// físicamente en local y luego se vuelve a crear con el mismo Code, nace con un Id nuevo
/// que choca (409) contra el índice único de branches.code de la nube.
/// </summary>
public interface IBranchIdReconciler
{
    /// <summary>Todo ocurre en una sola transacción: o se reasigna todo, o no cambia nada.
    /// Reasigna Employees, Devices, Attendances y la lista de sucursales de los Users.</summary>
    Task<BranchIdReassignResult> ReassignAsync(
        Guid currentId, Guid newId, CancellationToken cancellationToken = default);
}
