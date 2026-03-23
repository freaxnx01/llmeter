using LLMeter.Data;
using Microsoft.EntityFrameworkCore;

namespace LLMeter.Endpoints;

public static class UsageEndpoints
{
    public static void MapUsageEndpoints(this WebApplication app)
    {
        app.MapGet("/api/usage", async (string? provider, string period, LLMeterDbContext db) =>
        {
            var (from, to) = GetPeriodBounds(period);

            var query = db.UsageRecords
                .Where(u => u.RecordedAt >= from && u.RecordedAt < to);

            if (!string.IsNullOrEmpty(provider))
                query = query.Where(u => u.Provider == provider);

            var records = await query.ToListAsync();

            var grouped = records
                .GroupBy(r => r.Provider)
                .Select(g => new
                {
                    provider = g.Key,
                    inputTokens = g.Sum(r => r.InputTokens),
                    outputTokens = g.Sum(r => r.OutputTokens),
                    costUsd = g.Sum(r => r.CostUsd),
                    models = g.GroupBy(r => r.Model).Select(mg => new
                    {
                        model = mg.Key,
                        inputTokens = mg.Sum(r => r.InputTokens),
                        outputTokens = mg.Sum(r => r.OutputTokens),
                        costUsd = mg.Sum(r => r.CostUsd)
                    })
                });

            return Results.Ok(new
            {
                period,
                from,
                to,
                providers = grouped,
                totals = new
                {
                    inputTokens = records.Sum(r => r.InputTokens),
                    outputTokens = records.Sum(r => r.OutputTokens),
                    costUsd = records.Sum(r => r.CostUsd)
                }
            });
        });
    }

    private static (DateTime from, DateTime to) GetPeriodBounds(string period)
    {
        var now = DateTime.UtcNow;
        return period.ToLowerInvariant() switch
        {
            "day" => (now.Date, now.Date.AddDays(1)),
            "week" => GetIsoWeekBounds(now),
            "month" => (new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                        new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1)),
            "year" => (new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                       new DateTime(now.Year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            _ => (now.Date, now.Date.AddDays(1))
        };
    }

    private static (DateTime from, DateTime to) GetIsoWeekBounds(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        var monday = date.Date.AddDays(-diff);
        return (DateTime.SpecifyKind(monday, DateTimeKind.Utc),
                DateTime.SpecifyKind(monday.AddDays(7), DateTimeKind.Utc));
    }
}
