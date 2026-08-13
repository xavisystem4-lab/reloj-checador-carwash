using Microsoft.EntityFrameworkCore;
using RelojChecador.Application.Sync;

namespace RelojChecador.Infrastructure.Data.Sync;

public sealed class EfSyncCursorStore(RelojChecadorDbContext dbContext) : ISyncCursorStore
{
    public async Task<DateTime> GetCursorAsync(string entityType, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.Set<SyncCursorRecord>()
            .FirstOrDefaultAsync(c => c.EntityType == entityType, cancellationToken);
        return record?.CursorUtc ?? DateTime.MinValue;
    }

    public async Task SetCursorAsync(string entityType, DateTime valueUtc, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.Set<SyncCursorRecord>()
            .FirstOrDefaultAsync(c => c.EntityType == entityType, cancellationToken);
        if (record is null)
        {
            dbContext.Set<SyncCursorRecord>().Add(new SyncCursorRecord { EntityType = entityType, CursorUtc = valueUtc });
        }
        else
        {
            record.CursorUtc = valueUtc;
        }

        // Este store confirma su propio cambio en vez de esperar a que alguien más llame
        // SaveChangesAsync: el cursor es un detalle técnico de la sincronización, no parte
        // de la unidad de trabajo de ningún caso de uso de negocio — no tiene sentido que
        // su persistencia dependa de que otro código recuerde guardarla.
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
