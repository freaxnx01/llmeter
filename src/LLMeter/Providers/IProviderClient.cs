using LLMeter.Data;

namespace LLMeter.Providers;

public record SyncResult(
    List<UsageRecord> UsageRecords,
    BalanceSnapshot? Balance
);

public interface IProviderClient
{
    string ProviderName { get; }
    Task<SyncResult> SyncAsync(DateTime? lastSyncedAt, CancellationToken ct);
}
