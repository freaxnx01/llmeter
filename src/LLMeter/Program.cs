var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

app.Run();

// Make Program accessible for integration tests
public partial class Program { }
