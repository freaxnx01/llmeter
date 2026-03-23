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
