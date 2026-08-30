using RelojChecador.Domain.Payroll;

namespace RelojChecador.Application.Payroll;

public interface IPayrollDeductionRepository
{
    /// <summary>Todas las filas — usado por el motor de sincronización para el push
    /// completo a Supabase (mismo patrón que Branch/Employee/Device).</summary>
    Task<IReadOnlyList<PayrollDeduction>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Solo las filas de una semana — usado por la pantalla de Reportes.</summary>
    Task<IReadOnlyList<PayrollDeduction>> ListByWeekAsync(DateOnly weekStart, CancellationToken cancellationToken = default);

    /// <summary>Busca la fila de un empleado en una semana específica — usado para
    /// decidir si hay que crear una nueva o corregir la existente (ver
    /// PayrollViewModel.UpdateDeductionsAsync).</summary>
    Task<PayrollDeduction?> GetByEmployeeAndWeekAsync(Guid employeeId, DateOnly weekStart, CancellationToken cancellationToken = default);

    Task AddAsync(PayrollDeduction deduction, CancellationToken cancellationToken = default);

    /// <summary>Deducciones de un empleado en particular — usado para borrarlas antes de un
    /// borrado físico del empleado (ver IEmployeeRepository.RemoveAsync): a diferencia de
    /// Attendance, EmployeeId aquí es obligatorio (no se puede "desvincular"), así que un
    /// borrado físico del empleado también borra su historial de deducciones — pedido
    /// explícito del usuario, que entiende y acepta esa pérdida.</summary>
    Task<IReadOnlyList<PayrollDeduction>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task RemoveAsync(PayrollDeduction deduction, CancellationToken cancellationToken = default);
}
