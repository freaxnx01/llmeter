using System.Text.Json;
using LLMeter.Configuration;
using LLMeter.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LLMeter.Providers;

public class AnthropicClient : IProviderClient
{
    private readonly HttpClient _http;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicClient> _logger;

    public string ProviderName => "anthropic";

    public AnthropicClient(HttpClient http, IOptions<LLMeterOptions> options, ILogger<AnthropicClient> logger)
    {
        _http = http;
        _options = options.Value.Providers.Anthropic;
        _logger = logger;
    }

    public async Task<SyncResult> SyncAsync(DateTime? lastSyncedAt, CancellationToken ct)
    {
        var startingAt = lastSyncedAt ?? DateTime.UtcNow.AddHours(-24);
        var endingAt = DateTime.UtcNow;

        var costData = await FetchCostReport(startingAt, endingAt, ct);
        var usageData = await FetchUsageReport(startingAt, endingAt, ct);

        var records = new List<UsageRecord>();

        foreach (var cost in costData)
        {
            var model = cost.GetProperty("model").GetString() ?? "unknown";
            var costCents = cost.GetProperty("cost_cents").GetDouble();
            var costUsd = (decimal)(costCents / 100.0);
            var periodStart = cost.GetProperty("period_start").GetString() ?? "";

            long inputTokens = 0, outputTokens = 0;
            var usage = usageData.FirstOrDefault(u =>
                u.GetProperty("model").GetString() == model &&
                u.GetProperty("period_start").GetString() == periodStart);
            if (usage.ValueKind != JsonValueKind.Undefined)
            {
                inputTokens = usage.GetProperty("input_tokens").GetInt64();
                outputTokens = usage.GetProperty("output_tokens").GetInt64();
            }

            records.Add(new UsageRecord
            {
                Provider = ProviderName,
                Model = model,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CostUsd = costUsd,
                RecordedAt = DateTime.Parse(periodStart).ToUniversalTime(),
                SyncedAt = DateTime.UtcNow
            });
        }

        return new SyncResult(records, null);
    }

    private async Task<List<JsonElement>> FetchCostReport(DateTime from, DateTime to, CancellationToken ct)
    {
        var url = $"/v1/organizations/cost_report?starting_at={from:O}&ending_at={to:O}";
        return await FetchData(url, ct);
    }

    private async Task<List<JsonElement>> FetchUsageReport(DateTime from, DateTime to, CancellationToken ct)
    {
        var url = $"/v1/organizations/usage_report/messages?starting_at={from:O}&ending_at={to:O}";
        return await FetchData(url, ct);
    }

    private async Task<List<JsonElement>> FetchData(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-api-key", _options.AdminApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").EnumerateArray().ToList();
    }
}
