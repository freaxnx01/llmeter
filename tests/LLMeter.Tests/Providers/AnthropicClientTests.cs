using System.Net;
using System.Text.Json;
using LLMeter.Configuration;
using LLMeter.Providers;
using LLMeter.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LLMeter.Tests.Providers;

public class AnthropicClientTests
{
    [Fact]
    public async Task SyncAsync_ParsesCostReportAndUsageReport()
    {
        var costResponse = new
        {
            data = new[]
            {
                new { model = "claude-opus-4-6", cost_cents = 280.0, period_start = "2026-03-23T00:00:00Z", period_end = "2026-03-23T01:00:00Z" }
            }
        };
        var usageResponse = new
        {
            data = new[]
            {
                new { model = "claude-opus-4-6", input_tokens = 100000L, output_tokens = 40000L, period_start = "2026-03-23T00:00:00Z" }
            }
        };

        var handler = new FakeHttpHandler(new Dictionary<string, string>
        {
            ["/v1/organizations/cost_report"] = JsonSerializer.Serialize(costResponse),
            ["/v1/organizations/usage_report/messages"] = JsonSerializer.Serialize(usageResponse),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };

        var options = Options.Create(new LLMeterOptions
        {
            Providers = new ProvidersOptions
            {
                Anthropic = new AnthropicOptions { AdminApiKey = "test-key", TotalCredits = 100m }
            }
        });

        var client = new AnthropicClient(httpClient, options, NullLogger<AnthropicClient>.Instance);

        var result = await client.SyncAsync(null, CancellationToken.None);

        Assert.Single(result.UsageRecords);
        var record = result.UsageRecords[0];
        Assert.Equal("anthropic", record.Provider);
        Assert.Equal("claude-opus-4-6", record.Model);
        Assert.Equal(100000, record.InputTokens);
        Assert.Equal(40000, record.OutputTokens);
        Assert.Equal(2.80m, record.CostUsd);

        Assert.Null(result.Balance);
    }
}
