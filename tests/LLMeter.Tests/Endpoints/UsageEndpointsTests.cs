using System.Net.Http.Json;
using System.Text.Json;
using LLMeter.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LLMeter.Tests.Endpoints;

public class UsageEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly ServiceProvider _inMemoryServiceProvider = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .BuildServiceProvider();

    private readonly WebApplicationFactory<Program> _factory;

    public UsageEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove real DB and use in-memory
                var descriptors = services.Where(d =>
                    d.ServiceType == typeof(DbContextOptions<LLMeterDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(LLMeterDbContext) ||
                    (d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>))).ToList();
                foreach (var d in descriptors) services.Remove(d);

                services.AddDbContext<LLMeterDbContext>(o =>
                    o.UseInMemoryDatabase("test-usage")
                     .UseInternalServiceProvider(_inMemoryServiceProvider));

                // Remove background services
                var hostedServices = services.Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList();
                foreach (var s in hostedServices) services.Remove(s);
            });
        });
    }

    [Fact]
    public async Task GetUsage_ReturnsAggregatedData()
    {
        var client = _factory.CreateClient();

        // Seed data
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LLMeterDbContext>();
        db.UsageRecords.Add(new UsageRecord
        {
            Provider = "anthropic", Model = "claude-opus-4-6",
            InputTokens = 100000, OutputTokens = 40000, CostUsd = 2.80m,
            RecordedAt = DateTime.UtcNow, SyncedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var response = await client.GetAsync("/api/usage?period=day");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("day", json.GetProperty("period").GetString());
        Assert.True(json.GetProperty("totals").GetProperty("inputTokens").GetInt64() > 0);
    }
}
