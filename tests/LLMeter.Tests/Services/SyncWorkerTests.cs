using LLMeter.Configuration;
using LLMeter.Data;
using LLMeter.Providers;
using LLMeter.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LLMeter.Tests.Services;

public class SyncWorkerTests
{
    [Fact]
    public async Task SyncAllProviders_UpsertRecordsAndBalance()
    {
        var dbOptions = new DbContextOptionsBuilder<LLMeterDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new LLMeterDbContext(dbOptions);
        db.Database.EnsureCreated();

        var mockProvider = Substitute.For<IProviderClient>();
        mockProvider.ProviderName.Returns("test-provider");
        mockProvider.SyncAsync(Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new SyncResult(
                new List<UsageRecord>
                {
                    new() { Provider = "test-provider", Model = "test-model", InputTokens = 100, OutputTokens = 50, CostUsd = 0.5m, RecordedAt = DateTime.UtcNow }
                },
                new BalanceSnapshot { Provider = "test-provider", TotalCredits = 10m, TotalUsed = 0.5m, Remaining = 9.5m }
            ));

        var serviceProvider = Substitute.For<IServiceProvider>();
        var scope = Substitute.For<IServiceScope>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scopedProvider = Substitute.For<IServiceProvider>();

        scopeFactory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(scopedProvider);
        scopedProvider.GetService(typeof(LLMeterDbContext)).Returns(db);
        serviceProvider.GetService(typeof(IServiceScopeFactory)).Returns(scopeFactory);

        var options = Options.Create(new LLMeterOptions
        {
            SyncIntervalMinutes = 15,
            Providers = new ProvidersOptions
            {
                Anthropic = new AnthropicOptions { TotalCredits = 100m },
                Mistral = new MistralOptions { Budget = 20m }
            }
        });

        var worker = new SyncWorker(
            new[] { mockProvider },
            serviceProvider,
            options,
            NullLogger<SyncWorker>.Instance
        );

        await worker.SyncAllAsync(CancellationToken.None);

        Assert.Single(db.UsageRecords);
        Assert.Single(db.BalanceSnapshots);
        Assert.Single(db.SyncStatuses);
        Assert.Equal("test-provider", db.SyncStatuses.First().Provider);
    }
}
