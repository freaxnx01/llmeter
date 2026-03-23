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
