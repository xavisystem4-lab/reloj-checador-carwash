using Microsoft.EntityFrameworkCore;
using RelojChecador.Application.Branches;
using RelojChecador.Domain.Branches;

namespace RelojChecador.Infrastructure.Data.Repositories;

public sealed class EfBranchRepository(RelojChecadorDbContext dbContext) : IBranchRepository
{
    public Task<Branch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Branches.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public Task<Branch?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        dbContext.Branches.FirstOrDefaultAsync(b => b.Code == code.ToUpperInvariant(), cancellationToken);

    public async Task<IReadOnlyList<Branch>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Branches.OrderBy(b => b.Name).ToListAsync(cancellationToken);

    public async Task AddAsync(Branch branch, CancellationToken cancellationToken = default) =>
        await dbContext.Branches.AddAsync(branch, cancellationToken);

    public Task RemoveAsync(Branch branch, CancellationToken cancellationToken = default)
    {
        dbContext.Branches.Remove(branch);
        return Task.CompletedTask;
    }
}
