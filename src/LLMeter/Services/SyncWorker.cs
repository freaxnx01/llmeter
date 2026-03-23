using LLMeter.Configuration;
using LLMeter.Data;
using LLMeter.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LLMeter.Services;

public class SyncWorker : BackgroundService
{
    private readonly IEnumerable<IProviderClient> _providers;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SyncWorker> _logger;
    private readonly TimeSpan _interval;
    private readonly IOptions<LLMeterOptions> _options;

    public SyncWorker(
        IEnumerable<IProviderClient> providers,
        IServiceProvider serviceProvider,
        IOptions<LLMeterOptions> options,
        ILogger<SyncWorker> logger,
        TimeSpan? interval = null)
    {
        _providers = providers;
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromMinutes(options.Value.SyncIntervalMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await SyncAllAsync(stoppingToken);
            await Task.Delay(_interval, stoppingToken);
        }
    }

    public async Task SyncAllAsync(CancellationToken ct)
    {
        foreach (var provider in _providers)
        {
            try
            {
                await SyncProviderAsync(provider, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode >= System.Net.HttpStatusCode.InternalServerError)
            {
                _logger.LogWarning(ex, "Transient failure for {Provider}, retrying in 30s", provider.ProviderName);
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                try
                {
                    await SyncProviderAsync(provider, ct);
                }
                catch (Exception retryEx)
                {
                    _logger.LogError(retryEx, "Retry failed for {Provider}", provider.ProviderName);
                    await UpdateSyncStatus(provider.ProviderName, retryEx.Message, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync failed for {Provider}", provider.ProviderName);
                await UpdateSyncStatus(provider.ProviderName, ex.Message, ct);
            }
        }
    }

    private async Task SyncProviderAsync(IProviderClient provider, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LLMeterDbContext>();

        var syncStatus = await db.SyncStatuses.FindAsync(new object[] { provider.ProviderName }, ct);
        var lastSyncedAt = syncStatus?.LastSyncedAt;

        var result = await provider.SyncAsync(lastSyncedAt, ct);

        foreach (var record in result.UsageRecords)
        {
            var exists = await db.UsageRecords.AnyAsync(u =>
                u.RecordedAt == record.RecordedAt &&
                u.Provider == record.Provider &&
                u.Model == record.Model, ct);

            if (!exists)
                db.UsageRecords.Add(record);
        }

        if (result.Balance != null)
        {
            db.BalanceSnapshots.Add(result.Balance);
        }
        else
        {
            var cumulativeCost = await db.UsageRecords
                .Where(u => u.Provider == provider.ProviderName)
                .SumAsync(u => u.CostUsd, ct);

            var budget = provider.ProviderName switch
            {
                "anthropic" => _options.Value.Providers.Anthropic.TotalCredits,
                "mistral" => _options.Value.Providers.Mistral.Budget,
                _ => 0m
            };

            db.BalanceSnapshots.Add(new BalanceSnapshot
            {
                Provider = provider.ProviderName,
                TotalCredits = budget,
                TotalUsed = cumulativeCost,
                Remaining = budget - cumulativeCost,
                SnapshotAt = DateTime.UtcNow
            });
        }

        if (syncStatus == null)
        {
            db.SyncStatuses.Add(new SyncStatus
            {
                Provider = provider.ProviderName,
                LastSyncedAt = DateTime.UtcNow,
                LastError = null
            });
        }
        else
        {
            syncStatus.LastSyncedAt = DateTime.UtcNow;
            syncStatus.LastError = null;
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Synced {Count} records for {Provider}", result.UsageRecords.Count, provider.ProviderName);
    }

    private async Task UpdateSyncStatus(string provider, string error, CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LLMeterDbContext>();
            var status = await db.SyncStatuses.FindAsync(new object[] { provider }, ct);
            if (status != null)
            {
                status.LastError = error;
                await db.SaveChangesAsync(ct);
            }
            else
            {
                db.SyncStatuses.Add(new SyncStatus { Provider = provider, LastSyncedAt = DateTime.MinValue, LastError = error });
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update sync status for {Provider}", provider);
        }
    }
}
