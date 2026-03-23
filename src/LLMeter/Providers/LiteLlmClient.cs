using System.Text.Json;
using LLMeter.Configuration;
using LLMeter.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LLMeter.Providers;

public class LiteLlmClient : IProviderClient
{
    private readonly HttpClient _http;
    private readonly MistralOptions _options;
    private readonly ILogger<LiteLlmClient> _logger;

    private static readonly string[] MistralPrefixes = ["mistral", "codestral", "pixtral", "ministral"];

    public string ProviderName => "mistral";

    public LiteLlmClient(HttpClient http, IOptions<LLMeterOptions> options, ILogger<LiteLlmClient> logger)
    {
        _http = http;
        _options = options.Value.Providers.Mistral;
        _logger = logger;
    }

    public async Task<SyncResult> SyncAsync(DateTime? lastSyncedAt, CancellationToken ct)
    {
        var startTime = lastSyncedAt ?? DateTime.UtcNow.AddHours(-24);
        var url = $"/spend/logs?start_date={startTime:yyyy-MM-dd}";

        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var items = JsonDocument.Parse(json).RootElement.EnumerateArray().ToList();

        var records = new List<UsageRecord>();

        foreach (var item in items)
        {
            var model = item.GetProperty("model").GetString() ?? "";
            if (!IsMistralModel(model)) continue;

            var spend = (decimal)item.GetProperty("spend").GetDouble();

            records.Add(new UsageRecord
            {
                Provider = ProviderName,
                Model = model,
                InputTokens = item.GetProperty("prompt_tokens").GetInt64(),
                OutputTokens = item.GetProperty("completion_tokens").GetInt64(),
                CostUsd = spend,
                RecordedAt = DateTime.Parse(item.GetProperty("startTime").GetString()!).ToUniversalTime(),
                SyncedAt = DateTime.UtcNow
            });
        }

        return new SyncResult(records, null);
    }

    private static bool IsMistralModel(string model)
        => MistralPrefixes.Any(p => model.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
