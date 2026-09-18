using Microsoft.EntityFrameworkCore;
using RelojChecador.Application.Branches;

namespace RelojChecador.Infrastructure.Data.Repositories;

public sealed class EfBranchIdReconciler(RelojChecadorDbContext dbContext) : IBranchIdReconciler
{
    public async Task<BranchIdReassignResult> ReassignAsync(
        Guid currentId, Guid newId, CancellationToken cancellationToken = default)
    {
        if (currentId == newId)
        {
            return BranchIdReassignResult.SourceNotFound;
        }

        if (!await dbContext.Branches.AnyAsync(b => b.Id == currentId, cancellationToken))
        {
            return BranchIdReassignResult.SourceNotFound;
        }

        if (await dbContext.Branches.AnyAsync(b => b.Id == newId, cancellationToken))
        {
            return BranchIdReassignResult.TargetAlreadyExists;
        }

        // SQL directo y no entidades: Id es la llave primaria (EF no permite cambiarla en una
        // entidad ya rastreada) y esto no debe pasar por AuditableEntity — reasignar el Id
        // no es una edición del usuario, no debe mover UpdatedAtUtc ni ConcurrencyToken.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Employees SET BranchId = {newId} WHERE BranchId = {currentId}", cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Devices SET BranchId = {newId} WHERE BranchId = {currentId}", cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Attendances SET BranchId = {newId} WHERE BranchId = {currentId}", cancellationToken);

        // Users.BranchIds es una columna CSV de Guids en minúsculas (ver UserConfiguration),
        // no una columna de llave — se reemplaza el texto del Id viejo por el nuevo.
        var currentText = currentId.ToString();
        var newText = newId.ToString();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Users SET BranchIds = REPLACE(BranchIds, {currentText}, {newText}) WHERE BranchIds LIKE {"%" + currentText + "%"}",
            cancellationToken);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Branches SET Id = {newId} WHERE Id = {currentId}", cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        // Las entidades que este contexto ya tenía rastreadas conservan el Id viejo.
        dbContext.ChangeTracker.Clear();
        return BranchIdReassignResult.Reassigned;
    }
}
