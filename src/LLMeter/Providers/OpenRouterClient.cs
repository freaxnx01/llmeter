using System.Text.Json;
using LLMeter.Configuration;
using LLMeter.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LLMeter.Providers;

public class OpenRouterClient : IProviderClient
{
    private readonly HttpClient _http;
    private readonly OpenRouterOptions _options;
    private readonly ILogger<OpenRouterClient> _logger;

    public string ProviderName => "openrouter";

    public OpenRouterClient(HttpClient http, IOptions<LLMeterOptions> options, ILogger<OpenRouterClient> logger)
    {
        _http = http;
        _options = options.Value.Providers.OpenRouter;
        _logger = logger;
    }

    public async Task<SyncResult> SyncAsync(DateTime? lastSyncedAt, CancellationToken ct)
    {
        var balance = await FetchBalance(ct);
        var records = await FetchActivity(ct);
        return new SyncResult(records, balance);
    }

    private async Task<BalanceSnapshot> FetchBalance(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/credits");
        request.Headers.Add("Authorization", $"Bearer {_options.ManagementApiKey}");

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");

        var totalCredits = (decimal)data.GetProperty("total_credits").GetDouble();
        var totalUsage = (decimal)data.GetProperty("total_usage").GetDouble();

        return new BalanceSnapshot
        {
            Provider = ProviderName,
            TotalCredits = totalCredits,
            TotalUsed = totalUsage,
            Remaining = totalCredits - totalUsage,
            SnapshotAt = DateTime.UtcNow
        };
    }

    private async Task<List<UsageRecord>> FetchActivity(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/activity");
        request.Headers.Add("Authorization", $"Bearer {_options.ManagementApiKey}");

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        var records = new List<UsageRecord>();

        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            records.Add(new UsageRecord
            {
                Provider = ProviderName,
                Model = item.GetProperty("model").GetString() ?? "unknown",
                InputTokens = item.GetProperty("tokens_prompt").GetInt64(),
                OutputTokens = item.GetProperty("tokens_completion").GetInt64(),
                CostUsd = (decimal)item.GetProperty("usage").GetDouble(),
                RecordedAt = DateTime.Parse(item.GetProperty("created_at").GetString()!).ToUniversalTime(),
                SyncedAt = DateTime.UtcNow
            });
        }

        return records;
    }
}
