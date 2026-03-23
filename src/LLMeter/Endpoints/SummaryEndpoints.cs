using LLMeter.Data;
using Microsoft.EntityFrameworkCore;

namespace LLMeter.Endpoints;

public static class SummaryEndpoints
{
    public static void MapSummaryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/summary", async (LLMeterDbContext db) =>
        {
            var todayStart = DateTime.UtcNow.Date;
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var todayRecords = await db.UsageRecords
                .Where(u => u.RecordedAt >= todayStart)
                .ToListAsync();

            var monthRecords = await db.UsageRecords
                .Where(u => u.RecordedAt >= monthStart)
                .ToListAsync();

            var allBalances = await db.BalanceSnapshots.ToListAsync();
            var latestBalances = allBalances
                .GroupBy(b => b.Provider)
                .Select(g => g.OrderByDescending(b => b.SnapshotAt).First())
                .ToList();

            var syncStatuses = await db.SyncStatuses.ToListAsync();
            var oldestSync = syncStatuses.Any()
                ? syncStatuses.Min(s => s.LastSyncedAt)
                : (DateTime?)null;

            return Results.Ok(new
            {
                today = new
                {
                    costUsd = todayRecords.Sum(r => r.CostUsd),
                    inputTokens = todayRecords.Sum(r => r.InputTokens),
                    outputTokens = todayRecords.Sum(r => r.OutputTokens)
                },
                thisMonth = new
                {
                    costUsd = monthRecords.Sum(r => r.CostUsd),
                    inputTokens = monthRecords.Sum(r => r.InputTokens),
                    outputTokens = monthRecords.Sum(r => r.OutputTokens)
                },
                balanceTotal = latestBalances.Sum(b => b.Remaining),
                lastSyncedAt = oldestSync
            });
        });
    }
}
