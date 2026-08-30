using RelojChecador.Domain.Branches;

namespace RelojChecador.Application.Branches;

public interface IBranchRepository
{
    Task<Branch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Branch?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Branch>> ListAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Branch branch, CancellationToken cancellationToken = default);

    /// <summary>Borrado FÍSICO, no baja lógica (ver Branch.Deactivate para eso) — usado
    /// solo por el flujo explícito de "Borrar sucursales" (ver
    /// MainViewModel.HardDeleteBranchesAsync), que primero reasigna sus empleados,
    /// dispositivos y marcaciones a otra sucursal. Nunca se llama para dar de baja una
    /// sucursal en el uso normal del día a día.</summary>
    Task RemoveAsync(Branch branch, CancellationToken cancellationToken = default);
}
