using System.Net;
using System.Text.Json;
using LLMeter.Configuration;
using LLMeter.Providers;
using LLMeter.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LLMeter.Tests.Providers;

public class LiteLlmClientTests
{
    [Fact]
    public async Task SyncAsync_ParsesSpendLogs_FiltersToMistral()
    {
        var spendResponse = new[]
        {
            new { model = "mistral-large-latest", spend = 0.50, prompt_tokens = 30000, completion_tokens = 10000, startTime = "2026-03-23T10:00:00Z" },
            new { model = "claude-opus-4-6", spend = 2.00, prompt_tokens = 80000, completion_tokens = 30000, startTime = "2026-03-23T10:05:00Z" }
        };

        var handler = new FakeHttpHandler(new Dictionary<string, string>
        {
            ["/spend/logs"] = JsonSerializer.Serialize(spendResponse),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:4000") };

        var options = Options.Create(new LLMeterOptions
        {
            Providers = new ProvidersOptions
            {
                Mistral = new MistralOptions { LiteLlmBaseUrl = "http://localhost:4000", Budget = 20m }
            }
        });

        var client = new LiteLlmClient(httpClient, options, NullLogger<LiteLlmClient>.Instance);

        var result = await client.SyncAsync(null, CancellationToken.None);

        Assert.Single(result.UsageRecords);
        Assert.Equal("mistral", result.UsageRecords[0].Provider);
        Assert.Equal("mistral-large-latest", result.UsageRecords[0].Model);
        Assert.Equal(30000, result.UsageRecords[0].InputTokens);

        Assert.Null(result.Balance);
    }
}
