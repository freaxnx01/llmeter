# LLMeter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a self-hosted ASP.NET Core service that polls Anthropic, OpenRouter, and LiteLLM APIs for usage/cost data, stores it in SQLite, and exposes REST endpoints for StreamDeck and other consumers.

**Architecture:** Background sync worker polls three provider APIs every 15 minutes, upserts usage records and balance snapshots into SQLite. ASP.NET Core Minimal API serves aggregated data via `/api/usage`, `/api/balance`, `/api/summary`, and `/healthz`.

**Tech Stack:** .NET 8, ASP.NET Core Minimal API, EF Core + SQLite, Docker

**Spec:** `docs/specs/2026-03-23-llmeter-design.md`

---

## File Structure

```
llmeter/
├── src/
│   └── LLMeter/
│       ├── LLMeter.csproj
│       ├── Program.cs                          # App startup, DI, endpoint registration
│       ├── appsettings.json                    # Config with provider settings
│       ├── Configuration/
│       │   └── LLMeterOptions.cs               # Strongly-typed config binding
│       ├── Data/
│       │   ├── LLMeterDbContext.cs              # EF Core DbContext
│       │   ├── UsageRecord.cs                   # Entity
│       │   ├── BalanceSnapshot.cs               # Entity
│       │   └── SyncStatus.cs                    # Entity
│       ├── Providers/
│       │   ├── IProviderClient.cs               # Interface for provider sync
│       │   ├── AnthropicClient.cs               # Anthropic Admin API client
│       │   ├── OpenRouterClient.cs              # OpenRouter API client
│       │   └── LiteLlmClient.cs                # LiteLLM API client
│       ├── Services/
│       │   └── SyncWorker.cs                    # BackgroundService orchestrating sync
│       └── Endpoints/
│           ├── UsageEndpoints.cs                # GET /api/usage
│           ├── BalanceEndpoints.cs              # GET /api/balance
│           └── SummaryEndpoints.cs              # GET /api/summary + /healthz
├── tests/
│   └── LLMeter.Tests/
│       ├── LLMeter.Tests.csproj
│       ├── Helpers/
│       │   └── FakeHttpHandler.cs          # Shared test HTTP handler
│       ├── Providers/
│       │   ├── AnthropicClientTests.cs
│       │   ├── OpenRouterClientTests.cs
│       │   └── LiteLlmClientTests.cs
│       ├── Services/
│       │   └── SyncWorkerTests.cs
│       └── Endpoints/
│           ├── UsageEndpointsTests.cs
│           ├── BalanceEndpointsTests.cs
│           └── SummaryEndpointsTests.cs
├── Dockerfile
├── docker-compose.yml
└── LLMeter.sln
```

---

## Task 1: Project Scaffold

**Files:**
- Create: `LLMeter.sln`
- Create: `src/LLMeter/LLMeter.csproj`
- Create: `src/LLMeter/Program.cs`
- Create: `src/LLMeter/appsettings.json`
- Create: `tests/LLMeter.Tests/LLMeter.Tests.csproj`

- [ ] **Step 1: Create solution and projects**

```bash
cd /home/freax/projects/github-repos/llmeter
dotnet new sln -n LLMeter
mkdir -p src/LLMeter
dotnet new web -n LLMeter -o src/LLMeter --no-https
mkdir -p tests/LLMeter.Tests
dotnet new xunit -n LLMeter.Tests -o tests/LLMeter.Tests
dotnet sln add src/LLMeter/LLMeter.csproj
dotnet sln add tests/LLMeter.Tests/LLMeter.Tests.csproj
dotnet add tests/LLMeter.Tests/LLMeter.Tests.csproj reference src/LLMeter/LLMeter.csproj
```

- [ ] **Step 2: Add NuGet packages**

```bash
cd src/LLMeter
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
dotnet add package Microsoft.EntityFrameworkCore.Design
cd ../../tests/LLMeter.Tests
dotnet add package Microsoft.AspNetCore.Mvc.Testing
dotnet add package Microsoft.EntityFrameworkCore.InMemory
dotnet add package NSubstitute
```

- [ ] **Step 3: Write minimal Program.cs**

```csharp
// src/LLMeter/Program.cs
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

app.Run();

// Make Program accessible for integration tests
public partial class Program { }
```

- [ ] **Step 4: Write appsettings.json**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "SyncIntervalMinutes": 15,
  "Providers": {
    "Anthropic": {
      "AdminApiKey": "",
      "TotalCredits": 0.0
    },
    "OpenRouter": {
      "ManagementApiKey": ""
    },
    "Mistral": {
      "LiteLlmBaseUrl": "http://localhost:4000",
      "Budget": 0.0
    }
  }
}
```

- [ ] **Step 5: Verify it builds and runs**

```bash
cd /home/freax/projects/github-repos/llmeter
dotnet build
dotnet test
```

Expected: Build succeeds, default xUnit test passes.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: scaffold .NET solution with web API and test projects"
```

---

## Task 2: Configuration & Data Model

**Files:**
- Create: `src/LLMeter/Configuration/LLMeterOptions.cs`
- Create: `src/LLMeter/Data/UsageRecord.cs`
- Create: `src/LLMeter/Data/BalanceSnapshot.cs`
- Create: `src/LLMeter/Data/SyncStatus.cs`
- Create: `src/LLMeter/Data/LLMeterDbContext.cs`
- Modify: `src/LLMeter/Program.cs`

- [ ] **Step 1: Write configuration class**

```csharp
// src/LLMeter/Configuration/LLMeterOptions.cs
namespace LLMeter.Configuration;

public class LLMeterOptions
{
    public int SyncIntervalMinutes { get; set; } = 15;
    public ProvidersOptions Providers { get; set; } = new();
}

public class ProvidersOptions
{
    public AnthropicOptions Anthropic { get; set; } = new();
    public OpenRouterOptions OpenRouter { get; set; } = new();
    public MistralOptions Mistral { get; set; } = new();
}

public class AnthropicOptions
{
    public string AdminApiKey { get; set; } = "";
    public decimal TotalCredits { get; set; }
}

public class OpenRouterOptions
{
    public string ManagementApiKey { get; set; } = "";
}

public class MistralOptions
{
    public string LiteLlmBaseUrl { get; set; } = "http://localhost:4000";
    public decimal Budget { get; set; }
}
```

- [ ] **Step 2: Write entity classes**

```csharp
// src/LLMeter/Data/UsageRecord.cs
namespace LLMeter.Data;

public class UsageRecord
{
    public int Id { get; set; }
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public DateTime RecordedAt { get; set; }
    public DateTime SyncedAt { get; set; }
}
```

```csharp
// src/LLMeter/Data/BalanceSnapshot.cs
namespace LLMeter.Data;

public class BalanceSnapshot
{
    public int Id { get; set; }
    public string Provider { get; set; } = "";
    public decimal TotalCredits { get; set; }
    public decimal TotalUsed { get; set; }
    public decimal Remaining { get; set; }
    public DateTime SnapshotAt { get; set; }
}
```

```csharp
// src/LLMeter/Data/SyncStatus.cs
namespace LLMeter.Data;

public class SyncStatus
{
    public string Provider { get; set; } = "";
    public DateTime LastSyncedAt { get; set; }
    public string? LastError { get; set; }
}
```

- [ ] **Step 3: Write DbContext**

```csharp
// src/LLMeter/Data/LLMeterDbContext.cs
using Microsoft.EntityFrameworkCore;

namespace LLMeter.Data;

public class LLMeterDbContext : DbContext
{
    public LLMeterDbContext(DbContextOptions<LLMeterDbContext> options) : base(options) { }

    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();
    public DbSet<BalanceSnapshot> BalanceSnapshots => Set<BalanceSnapshot>();
    public DbSet<SyncStatus> SyncStatuses => Set<SyncStatus>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UsageRecord>()
            .HasIndex(u => new { u.RecordedAt, u.Provider, u.Model })
            .IsUnique();

        modelBuilder.Entity<SyncStatus>()
            .HasKey(s => s.Provider);
    }
}
```

- [ ] **Step 4: Register in Program.cs**

```csharp
// src/LLMeter/Program.cs
using LLMeter.Configuration;
using LLMeter.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LLMeterOptions>(builder.Configuration);
builder.Services.AddDbContext<LLMeterDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=llmeter.db"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LLMeterDbContext>();
    db.Database.EnsureCreated();
}

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

app.Run();

public partial class Program { }
```

- [ ] **Step 5: Verify build**

```bash
dotnet build
```

Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add configuration classes, EF Core entities, and DbContext"
```

---

## Task 3: Provider Clients – Interface & Anthropic

**Files:**
- Create: `src/LLMeter/Providers/IProviderClient.cs`
- Create: `src/LLMeter/Providers/AnthropicClient.cs`
- Create: `tests/LLMeter.Tests/Providers/AnthropicClientTests.cs`

- [ ] **Step 1: Write provider interface**

```csharp
// src/LLMeter/Providers/IProviderClient.cs
using LLMeter.Data;

namespace LLMeter.Providers;

public record SyncResult(
    List<UsageRecord> UsageRecords,
    BalanceSnapshot? Balance
);

// Note: For providers with config-based balance (Anthropic, Mistral),
// the SyncWorker calculates cumulative cost from DB after upserting records,
// then creates the BalanceSnapshot. Provider clients return Balance = null
// for these providers, and SyncWorker handles it.

public interface IProviderClient
{
    string ProviderName { get; }
    Task<SyncResult> SyncAsync(DateTime? lastSyncedAt, CancellationToken ct);
}
```

- [ ] **Step 2: Write Anthropic client test**

```csharp
// tests/LLMeter.Tests/Providers/AnthropicClientTests.cs
using System.Net;
using System.Text.Json;
using LLMeter.Configuration;
using LLMeter.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LLMeter.Tests.Providers;

public class AnthropicClientTests
{
    [Fact]
    public async Task SyncAsync_ParsesCostReportAndUsageReport()
    {
        // Arrange
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

        // Act
        var result = await client.SyncAsync(null, CancellationToken.None);

        // Assert
        Assert.Single(result.UsageRecords);
        var record = result.UsageRecords[0];
        Assert.Equal("anthropic", record.Provider);
        Assert.Equal("claude-opus-4-6", record.Model);
        Assert.Equal(100000, record.InputTokens);
        Assert.Equal(40000, record.OutputTokens);
        Assert.Equal(2.80m, record.CostUsd);

        // Balance is null — SyncWorker calculates it from cumulative DB cost
        Assert.Null(result.Balance);
    }
}

// FakeHttpHandler is in tests/LLMeter.Tests/Helpers/FakeHttpHandler.cs (created in this task)
```

Create the shared test helper:

```csharp
// tests/LLMeter.Tests/Helpers/FakeHttpHandler.cs
using System.Net;

namespace LLMeter.Tests.Helpers;

public class FakeHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, string> _responses;

    public FakeHttpHandler(Dictionary<string, string> responses)
    {
        _responses = responses;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var match = _responses.FirstOrDefault(kvp => path.StartsWith(kvp.Key));
        var content = match.Value ?? "{}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
        });
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

```bash
dotnet test tests/LLMeter.Tests --filter "AnthropicClientTests" -v n
```

Expected: FAIL — `AnthropicClient` does not exist yet.

- [ ] **Step 4: Implement AnthropicClient**

```csharp
// src/LLMeter/Providers/AnthropicClient.cs
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

        // Balance is null — SyncWorker calculates cumulative balance from DB
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
```

- [ ] **Step 5: Run test to verify it passes**

```bash
dotnet test tests/LLMeter.Tests --filter "AnthropicClientTests" -v n
```

Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add IProviderClient interface and AnthropicClient with tests"
```

---

## Task 4: Provider Clients – OpenRouter

**Files:**
- Create: `src/LLMeter/Providers/OpenRouterClient.cs`
- Create: `tests/LLMeter.Tests/Providers/OpenRouterClientTests.cs`

- [ ] **Step 1: Write OpenRouter client test**

```csharp
// tests/LLMeter.Tests/Providers/OpenRouterClientTests.cs
using System.Net;
using System.Text.Json;
using LLMeter.Configuration;
using LLMeter.Providers;
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
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/LLMeter.Tests --filter "OpenRouterClientTests" -v n
```

Expected: FAIL — `OpenRouterClient` does not exist.

- [ ] **Step 3: Implement OpenRouterClient**

```csharp
// src/LLMeter/Providers/OpenRouterClient.cs
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
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test tests/LLMeter.Tests --filter "OpenRouterClientTests" -v n
```

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add OpenRouterClient with tests"
```

---

## Task 5: Provider Clients – LiteLLM

**Files:**
- Create: `src/LLMeter/Providers/LiteLlmClient.cs`
- Create: `tests/LLMeter.Tests/Providers/LiteLlmClientTests.cs`

- [ ] **Step 1: Write LiteLLM client test**

```csharp
// tests/LLMeter.Tests/Providers/LiteLlmClientTests.cs
using System.Net;
using System.Text.Json;
using LLMeter.Configuration;
using LLMeter.Providers;
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

        // Balance is null — SyncWorker calculates it from cumulative DB cost
        Assert.Null(result.Balance);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/LLMeter.Tests --filter "LiteLlmClientTests" -v n
```

Expected: FAIL — `LiteLlmClient` does not exist.

- [ ] **Step 3: Implement LiteLlmClient**

```csharp
// src/LLMeter/Providers/LiteLlmClient.cs
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

        // Balance is null — SyncWorker calculates cumulative balance from DB
        return new SyncResult(records, null);
    }

    private static bool IsMistralModel(string model)
        => MistralPrefixes.Any(p => model.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test tests/LLMeter.Tests --filter "LiteLlmClientTests" -v n
```

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add LiteLlmClient with Mistral model filtering and tests"
```

---

## Task 6: Sync Worker

**Files:**
- Create: `src/LLMeter/Services/SyncWorker.cs`
- Create: `tests/LLMeter.Tests/Services/SyncWorkerTests.cs`
- Modify: `src/LLMeter/Program.cs`

- [ ] **Step 1: Write SyncWorker test**

```csharp
// tests/LLMeter.Tests/Services/SyncWorkerTests.cs
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
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/LLMeter.Tests --filter "SyncWorkerTests" -v n
```

Expected: FAIL — `SyncWorker` does not exist.

- [ ] **Step 3: Implement SyncWorker**

```csharp
// src/LLMeter/Services/SyncWorker.cs
using LLMeter.Data;
using LLMeter.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LLMeter.Services;

public class SyncWorker : BackgroundService
{
    private readonly IEnumerable<IProviderClient> _providers;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SyncWorker> _logger;
    private readonly TimeSpan _interval;

    private readonly IOptions<LLMeterOptions> _options;

    public SyncWorker(
        IEnumerable<IProviderClient> providers,
        IServiceProvider serviceProvider,
        IOptions<LLMeterOptions> options,
        ILogger<SyncWorker> logger,
        TimeSpan? interval = null)
    {
        _providers = providers;
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromMinutes(options.Value.SyncIntervalMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await SyncAllAsync(stoppingToken);
            await Task.Delay(_interval, stoppingToken);
        }
    }

    public async Task SyncAllAsync(CancellationToken ct)
    {
        foreach (var provider in _providers)
        {
            try
            {
                await SyncProviderAsync(provider, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode >= System.Net.HttpStatusCode.InternalServerError)
            {
                _logger.LogWarning(ex, "Transient failure for {Provider}, retrying in 30s", provider.ProviderName);
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                try
                {
                    await SyncProviderAsync(provider, ct);
                }
                catch (Exception retryEx)
                {
                    _logger.LogError(retryEx, "Retry failed for {Provider}", provider.ProviderName);
                    await UpdateSyncStatus(provider.ProviderName, retryEx.Message, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync failed for {Provider}", provider.ProviderName);
                await UpdateSyncStatus(provider.ProviderName, ex.Message, ct);
            }
        }
    }

    private async Task SyncProviderAsync(IProviderClient provider, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LLMeterDbContext>();

        var syncStatus = await db.SyncStatuses.FindAsync(new object[] { provider.ProviderName }, ct);
        var lastSyncedAt = syncStatus?.LastSyncedAt;

        var result = await provider.SyncAsync(lastSyncedAt, ct);

        foreach (var record in result.UsageRecords)
        {
            var exists = await db.UsageRecords.AnyAsync(u =>
                u.RecordedAt == record.RecordedAt &&
                u.Provider == record.Provider &&
                u.Model == record.Model, ct);

            if (!exists)
                db.UsageRecords.Add(record);
        }

        // If provider returned a balance (e.g. OpenRouter), use it directly
        if (result.Balance != null)
        {
            db.BalanceSnapshots.Add(result.Balance);
        }
        else
        {
            // For config-based balance (Anthropic, Mistral): calculate from cumulative DB cost
            var cumulativeCost = await db.UsageRecords
                .Where(u => u.Provider == provider.ProviderName)
                .SumAsync(u => u.CostUsd, ct);

            var budget = provider.ProviderName switch
            {
                "anthropic" => _options.Value.Providers.Anthropic.TotalCredits,
                "mistral" => _options.Value.Providers.Mistral.Budget,
                _ => 0m
            };

            db.BalanceSnapshots.Add(new BalanceSnapshot
            {
                Provider = provider.ProviderName,
                TotalCredits = budget,
                TotalUsed = cumulativeCost,
                Remaining = budget - cumulativeCost,
                SnapshotAt = DateTime.UtcNow
            });
        }

        if (syncStatus == null)
        {
            db.SyncStatuses.Add(new SyncStatus
            {
                Provider = provider.ProviderName,
                LastSyncedAt = DateTime.UtcNow,
                LastError = null
            });
        }
        else
        {
            syncStatus.LastSyncedAt = DateTime.UtcNow;
            syncStatus.LastError = null;
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Synced {Count} records for {Provider}", result.UsageRecords.Count, provider.ProviderName);
    }

    private async Task UpdateSyncStatus(string provider, string error, CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LLMeterDbContext>();
            var status = await db.SyncStatuses.FindAsync(new object[] { provider }, ct);
            if (status != null)
            {
                status.LastError = error;
                await db.SaveChangesAsync(ct);
            }
            else
            {
                db.SyncStatuses.Add(new SyncStatus { Provider = provider, LastSyncedAt = DateTime.MinValue, LastError = error });
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update sync status for {Provider}", provider);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
dotnet test tests/LLMeter.Tests --filter "SyncWorkerTests" -v n
```

Expected: PASS

- [ ] **Step 5: Register services in Program.cs**

Add to `Program.cs` after the DbContext registration:

```csharp
// Register provider clients as typed HttpClient services
builder.Services.AddHttpClient<IProviderClient, AnthropicClient>("Anthropic", client =>
    client.BaseAddress = new Uri("https://api.anthropic.com"));
builder.Services.AddHttpClient<IProviderClient, OpenRouterClient>("OpenRouter", client =>
    client.BaseAddress = new Uri("https://openrouter.ai"));
builder.Services.AddHttpClient<IProviderClient, LiteLlmClient>("LiteLlm", (sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<LLMeterOptions>>();
    client.BaseAddress = new Uri(opts.Value.Providers.Mistral.LiteLlmBaseUrl);
});

builder.Services.AddHostedService<SyncWorker>();
```

Add these using statements to `Program.cs`:

```csharp
using LLMeter.Providers;
using LLMeter.Services;
using Microsoft.Extensions.Options;
```

- [ ] **Step 6: Verify build**

```bash
dotnet build
```

Expected: Build succeeds.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add SyncWorker background service with provider orchestration"
```

---

## Task 7: API Endpoints – Usage

**Files:**
- Create: `src/LLMeter/Endpoints/UsageEndpoints.cs`
- Create: `tests/LLMeter.Tests/Endpoints/UsageEndpointsTests.cs`
- Modify: `src/LLMeter/Program.cs`

- [ ] **Step 1: Write usage endpoint test**

```csharp
// tests/LLMeter.Tests/Endpoints/UsageEndpointsTests.cs
using System.Net.Http.Json;
using System.Text.Json;
using LLMeter.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LLMeter.Tests.Endpoints;

public class UsageEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public UsageEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove real DB and use in-memory
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<LLMeterDbContext>));
                if (descriptor != null) services.Remove(descriptor);
                services.AddDbContext<LLMeterDbContext>(o => o.UseInMemoryDatabase("test-usage"));

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
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test tests/LLMeter.Tests --filter "UsageEndpointsTests" -v n
```

Expected: FAIL — endpoint not registered.

- [ ] **Step 3: Implement UsageEndpoints**

```csharp
// src/LLMeter/Endpoints/UsageEndpoints.cs
using LLMeter.Data;
using Microsoft.EntityFrameworkCore;

namespace LLMeter.Endpoints;

public static class UsageEndpoints
{
    public static void MapUsageEndpoints(this WebApplication app)
    {
        app.MapGet("/api/usage", async (string? provider, string period, LLMeterDbContext db) =>
        {
            var (from, to) = GetPeriodBounds(period);

            var query = db.UsageRecords
                .Where(u => u.RecordedAt >= from && u.RecordedAt < to);

            if (!string.IsNullOrEmpty(provider))
                query = query.Where(u => u.Provider == provider);

            var records = await query.ToListAsync();

            var grouped = records
                .GroupBy(r => r.Provider)
                .Select(g => new
                {
                    provider = g.Key,
                    inputTokens = g.Sum(r => r.InputTokens),
                    outputTokens = g.Sum(r => r.OutputTokens),
                    costUsd = g.Sum(r => r.CostUsd),
                    models = g.GroupBy(r => r.Model).Select(mg => new
                    {
                        model = mg.Key,
                        inputTokens = mg.Sum(r => r.InputTokens),
                        outputTokens = mg.Sum(r => r.OutputTokens),
                        costUsd = mg.Sum(r => r.CostUsd)
                    })
                });

            return Results.Ok(new
            {
                period,
                from,
                to,
                providers = grouped,
                totals = new
                {
                    inputTokens = records.Sum(r => r.InputTokens),
                    outputTokens = records.Sum(r => r.OutputTokens),
                    costUsd = records.Sum(r => r.CostUsd)
                }
            });
        });
    }

    private static (DateTime from, DateTime to) GetPeriodBounds(string period)
    {
        var now = DateTime.UtcNow;
        return period.ToLowerInvariant() switch
        {
            "day" => (now.Date, now.Date.AddDays(1)),
            "week" => GetIsoWeekBounds(now),
            "month" => (new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                        new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1)),
            "year" => (new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                       new DateTime(now.Year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            _ => (now.Date, now.Date.AddDays(1))
        };
    }

    private static (DateTime from, DateTime to) GetIsoWeekBounds(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        var monday = date.Date.AddDays(-diff);
        return (DateTime.SpecifyKind(monday, DateTimeKind.Utc),
                DateTime.SpecifyKind(monday.AddDays(7), DateTimeKind.Utc));
    }
}
```

- [ ] **Step 4: Register in Program.cs**

Add before `app.Run();`:

```csharp
using LLMeter.Endpoints;
// ...
app.MapUsageEndpoints();
```

- [ ] **Step 5: Run test to verify it passes**

```bash
dotnet test tests/LLMeter.Tests --filter "UsageEndpointsTests" -v n
```

Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add GET /api/usage endpoint with period aggregation"
```

---

## Task 8: API Endpoints – Balance & Summary

**Files:**
- Create: `src/LLMeter/Endpoints/BalanceEndpoints.cs`
- Create: `src/LLMeter/Endpoints/SummaryEndpoints.cs`
- Create: `tests/LLMeter.Tests/Endpoints/BalanceEndpointsTests.cs`
- Create: `tests/LLMeter.Tests/Endpoints/SummaryEndpointsTests.cs`
- Modify: `src/LLMeter/Program.cs`

- [ ] **Step 1: Write balance endpoint test**

```csharp
// tests/LLMeter.Tests/Endpoints/BalanceEndpointsTests.cs
using System.Net.Http.Json;
using System.Text.Json;
using LLMeter.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LLMeter.Tests.Endpoints;

public class BalanceEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BalanceEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<LLMeterDbContext>));
                if (descriptor != null) services.Remove(descriptor);
                services.AddDbContext<LLMeterDbContext>(o => o.UseInMemoryDatabase("test-balance"));

                var hostedServices = services.Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList();
                foreach (var s in hostedServices) services.Remove(s);
            });
        });
    }

    [Fact]
    public async Task GetBalance_ReturnsLatestSnapshots()
    {
        var client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LLMeterDbContext>();
        db.BalanceSnapshots.Add(new BalanceSnapshot
        {
            Provider = "anthropic", TotalCredits = 100m, TotalUsed = 45m, Remaining = 55m,
            SnapshotAt = DateTime.UtcNow
        });
        db.SyncStatuses.Add(new SyncStatus { Provider = "anthropic", LastSyncedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var response = await client.GetAsync("/api/balance");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var providers = json.GetProperty("providers");
        Assert.True(providers.GetArrayLength() > 0);
    }
}
```

- [ ] **Step 2: Write summary endpoint test**

```csharp
// tests/LLMeter.Tests/Endpoints/SummaryEndpointsTests.cs
using System.Net.Http.Json;
using System.Text.Json;
using LLMeter.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LLMeter.Tests.Endpoints;

public class SummaryEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SummaryEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<LLMeterDbContext>));
                if (descriptor != null) services.Remove(descriptor);
                services.AddDbContext<LLMeterDbContext>(o => o.UseInMemoryDatabase("test-summary"));

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
```

- [ ] **Step 3: Run tests to verify they fail**

```bash
dotnet test tests/LLMeter.Tests --filter "BalanceEndpointsTests|SummaryEndpointsTests" -v n
```

Expected: FAIL — endpoints not registered.

- [ ] **Step 4: Implement BalanceEndpoints**

```csharp
// src/LLMeter/Endpoints/BalanceEndpoints.cs
using LLMeter.Data;
using Microsoft.EntityFrameworkCore;

namespace LLMeter.Endpoints;

public static class BalanceEndpoints
{
    public static void MapBalanceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/balance", async (LLMeterDbContext db) =>
        {
            // Load all snapshots and group client-side (SQLite doesn't support GroupBy+First in EF Core)
            var allSnapshots = await db.BalanceSnapshots.ToListAsync();
            var latestSnapshots = allSnapshots
                .GroupBy(b => b.Provider)
                .Select(g => g.OrderByDescending(b => b.SnapshotAt).First())
                .ToList();

            var syncStatuses = await db.SyncStatuses.ToDictionaryAsync(s => s.Provider, s => s.LastSyncedAt);

            return Results.Ok(new
            {
                providers = latestSnapshots.Select(s => new
                {
                    provider = s.Provider,
                    totalCredits = s.TotalCredits,
                    used = s.TotalUsed,
                    remaining = s.Remaining
                }),
                lastSyncedAt = syncStatuses
            });
        });
    }
}
```

- [ ] **Step 5: Implement SummaryEndpoints**

```csharp
// src/LLMeter/Endpoints/SummaryEndpoints.cs
using LLMeter.Data;
using Microsoft.EntityFrameworkCore;

namespace LLMeter.Endpoints;

public static class SummaryEndpoints
{
    public static void MapSummaryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/summary", async (LLMeterDbContext db) =>
        {
            var todayStart = DateTime.UtcNow.Date;
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var todayRecords = await db.UsageRecords
                .Where(u => u.RecordedAt >= todayStart)
                .ToListAsync();

            var monthRecords = await db.UsageRecords
                .Where(u => u.RecordedAt >= monthStart)
                .ToListAsync();

            var allBalances = await db.BalanceSnapshots.ToListAsync();
            var latestBalances = allBalances
                .GroupBy(b => b.Provider)
                .Select(g => g.OrderByDescending(b => b.SnapshotAt).First())
                .ToList();

            var syncStatuses = await db.SyncStatuses.ToListAsync();
            var oldestSync = syncStatuses.Any()
                ? syncStatuses.Min(s => s.LastSyncedAt)
                : (DateTime?)null;

            return Results.Ok(new
            {
                today = new
                {
                    costUsd = todayRecords.Sum(r => r.CostUsd),
                    inputTokens = todayRecords.Sum(r => r.InputTokens),
                    outputTokens = todayRecords.Sum(r => r.OutputTokens)
                },
                thisMonth = new
                {
                    costUsd = monthRecords.Sum(r => r.CostUsd),
                    inputTokens = monthRecords.Sum(r => r.InputTokens),
                    outputTokens = monthRecords.Sum(r => r.OutputTokens)
                },
                balanceTotal = latestBalances.Sum(b => b.Remaining),
                lastSyncedAt = oldestSync
            });
        });
    }
}
```

- [ ] **Step 6: Register in Program.cs**

Add before `app.Run();`:

```csharp
app.MapBalanceEndpoints();
app.MapSummaryEndpoints();
```

- [ ] **Step 7: Run tests to verify they pass**

```bash
dotnet test tests/LLMeter.Tests --filter "BalanceEndpointsTests|SummaryEndpointsTests" -v n
```

Expected: PASS

- [ ] **Step 8: Run all tests**

```bash
dotnet test
```

Expected: All tests pass.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: add GET /api/balance and GET /api/summary endpoints"
```

---

## Task 9: Docker & Deployment

**Files:**
- Create: `Dockerfile`
- Create: `docker-compose.yml`

- [ ] **Step 1: Create Dockerfile**

```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY LLMeter.sln .
COPY src/LLMeter/LLMeter.csproj src/LLMeter/
COPY tests/LLMeter.Tests/LLMeter.Tests.csproj tests/LLMeter.Tests/
RUN dotnet restore
COPY . .
RUN dotnet publish src/LLMeter/LLMeter.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "LLMeter.dll"]
```

- [ ] **Step 2: Create docker-compose.yml**

```yaml
# docker-compose.yml
services:
  llmeter:
    build: .
    container_name: llmeter
    ports:
      - "8080:8080"
    volumes:
      - llmeter-data:/app/data
    environment:
      - ConnectionStrings__DefaultConnection=Data Source=/app/data/llmeter.db
      - Providers__Anthropic__AdminApiKey=${ANTHROPIC_ADMIN_API_KEY}
      - Providers__Anthropic__TotalCredits=${ANTHROPIC_TOTAL_CREDITS:-100}
      - Providers__OpenRouter__ManagementApiKey=${OPENROUTER_MANAGEMENT_API_KEY}
      - Providers__Mistral__LiteLlmBaseUrl=${LITELLM_BASE_URL:-http://litellm:4000}
      - Providers__Mistral__Budget=${MISTRAL_BUDGET:-20}
    restart: unless-stopped

volumes:
  llmeter-data:
```

- [ ] **Step 3: Verify Docker build**

```bash
docker build -t llmeter .
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add Dockerfile docker-compose.yml
git commit -m "feat: add Dockerfile and docker-compose.yml for deployment"
```

- [ ] **Step 5: Push to GitHub**

```bash
git push
```

---

## Task 10: Deploy to Proxmox

- [ ] **Step 1: Deploy with deploy-service.sh**

```bash
/home/freax/projects/mydocker-compose/deploy-service.sh --name llmeter --node cirrus-pve --url https://github.com/freaxnx01/llmeter
```

- [ ] **Step 2: Verify health endpoint**

```bash
curl http://llmeter.home.freaxnx01.ch/healthz
```

Expected: `{"status":"healthy"}`

- [ ] **Step 3: Verify API endpoints**

```bash
curl http://llmeter.home.freaxnx01.ch/api/summary
curl http://llmeter.home.freaxnx01.ch/api/balance
curl http://llmeter.home.freaxnx01.ch/api/usage?period=day
```

Expected: JSON responses (may be empty until first sync completes).
