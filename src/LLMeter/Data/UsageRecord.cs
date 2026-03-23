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
