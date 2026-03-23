using System.Net;
using System.Text.Json;
using LLMeter.Configuration;
using LLMeter.Providers;
using LLMeter.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LLMeter.Tests.Providers;

public class OpenRouterClientTests
{
    [Fact]
    public async Task SyncAsync_ParsesCreditsAndActivity()
    {
        var creditsResponse = new { data = new { total_credits = 50.0, total_usage = 12.80 } };
        var activityResponse = new
        {
            data = new[]
            {
                new { model = "anthropic/claude-opus-4-6", usage = 1.50, tokens_prompt = 50000, tokens_completion = 20000, created_at = "2026-03-23T10:00:00Z" }
            }
        };

        var handler = new FakeHttpHandler(new Dictionary<string, string>
        {
            ["/api/v1/credits"] = JsonSerializer.Serialize(creditsResponse),
            ["/api/v1/activity"] = JsonSerializer.Serialize(activityResponse),
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://openrouter.ai") };

        var options = Options.Create(new LLMeterOptions
        {
            Providers = new ProvidersOptions
            {
                OpenRouter = new OpenRouterOptions { ManagementApiKey = "test-key" }
            }
        });

        var client = new OpenRouterClient(httpClient, options, NullLogger<OpenRouterClient>.Instance);

        var result = await client.SyncAsync(null, CancellationToken.None);

        Assert.Single(result.UsageRecords);
        Assert.Equal("openrouter", result.UsageRecords[0].Provider);
        Assert.Equal(50000, result.UsageRecords[0].InputTokens);

        Assert.NotNull(result.Balance);
        Assert.Equal(50m, result.Balance!.TotalCredits);
        Assert.Equal(12.80m, result.Balance.TotalUsed);
        Assert.Equal(37.20m, result.Balance.Remaining);
    }
}
