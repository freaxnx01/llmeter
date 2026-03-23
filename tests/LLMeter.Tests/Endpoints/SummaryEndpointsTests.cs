using System.Net.Http.Json;
using System.Text.Json;
using LLMeter.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LLMeter.Tests.Endpoints;

public class SummaryEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly ServiceProvider _inMemoryServiceProvider = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .BuildServiceProvider();

    private readonly WebApplicationFactory<Program> _factory;

    public SummaryEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptors = services.Where(d =>
                    d.ServiceType == typeof(DbContextOptions<LLMeterDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(LLMeterDbContext) ||
                    (d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>))).ToList();
                foreach (var d in descriptors) services.Remove(d);

                services.AddDbContext<LLMeterDbContext>(o =>
                    o.UseInMemoryDatabase("test-summary")
                     .UseInternalServiceProvider(_inMemoryServiceProvider));

                var hostedServices = services.Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList();
                foreach (var s in hostedServices) services.Remove(s);
            });
        });
    }

    [Fact]
    public async Task GetSummary_ReturnsCompactData()
    {
        var client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LLMeterDbContext>();
        db.UsageRecords.Add(new UsageRecord
        {
            Provider = "anthropic", Model = "claude-opus-4-6",
            InputTokens = 80000, OutputTokens = 32000, CostUsd = 2.15m,
            RecordedAt = DateTime.UtcNow, SyncedAt = DateTime.UtcNow
        });
        db.BalanceSnapshots.Add(new BalanceSnapshot
        {
            Provider = "anthropic", TotalCredits = 100m, TotalUsed = 45m, Remaining = 55m,
            SnapshotAt = DateTime.UtcNow
        });
        db.SyncStatuses.Add(new SyncStatus { Provider = "anthropic", LastSyncedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var response = await client.GetAsync("/api/summary");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("today").GetProperty("costUsd").GetDecimal() > 0);
        Assert.True(json.GetProperty("balanceTotal").GetDecimal() > 0);
    }
}
