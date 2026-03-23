using LLMeter.Data;
using Microsoft.EntityFrameworkCore;

namespace LLMeter.Endpoints;

public static class BalanceEndpoints
{
    public static void MapBalanceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/balance", async (LLMeterDbContext db) =>
        {
            var allSnapshots = await db.BalanceSnapshots.ToListAsync();
            var latestSnapshots = allSnapshots
                .GroupBy(b => b.Provider)
                .Select(g => g.OrderByDescending(b => b.SnapshotAt).First())
                .ToList();

            var syncStatuses = await db.SyncStatuses.ToDictionaryAsync(s => s.Provider, s => s.LastSyncedAt);

            return Results.Ok(new
            {
                providers = latestSnapshots.Select(s => new
                {
                    provider = s.Provider,
                    totalCredits = s.TotalCredits,
                    used = s.TotalUsed,
                    remaining = s.Remaining
                }),
                lastSyncedAt = syncStatuses
            });
        });
    }
}
