using LLMeter.Configuration;
using LLMeter.Data;
using LLMeter.Endpoints;
using LLMeter.Providers;
using LLMeter.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LLMeterOptions>(builder.Configuration);
builder.Services.AddDbContext<LLMeterDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=llmeter.db"));

// Register provider clients
builder.Services.AddHttpClient<AnthropicClient>(client =>
    client.BaseAddress = new Uri("https://api.anthropic.com"));
builder.Services.AddSingleton<IProviderClient>(sp => sp.GetRequiredService<AnthropicClient>());

builder.Services.AddHttpClient<OpenRouterClient>(client =>
    client.BaseAddress = new Uri("https://openrouter.ai"));
builder.Services.AddSingleton<IProviderClient>(sp => sp.GetRequiredService<OpenRouterClient>());

builder.Services.AddHttpClient<LiteLlmClient>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    client.BaseAddress = new Uri(config["Providers:Mistral:LiteLlmBaseUrl"] ?? "http://localhost:4000");
});
builder.Services.AddSingleton<IProviderClient>(sp => sp.GetRequiredService<LiteLlmClient>());

builder.Services.AddHostedService<SyncWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<LLMeterDbContext>>();
    var isSqlite = options.Extensions.Any(e => e.GetType().Name.Contains("Sqlite"));
    if (isSqlite)
    {
        var db = scope.ServiceProvider.GetRequiredService<LLMeterDbContext>();
        db.Database.EnsureCreated();
    }
}

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

app.MapUsageEndpoints();

app.Run();

public partial class Program { }
