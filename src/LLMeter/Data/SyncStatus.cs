namespace LLMeter.Data;

public class SyncStatus
{
    public string Provider { get; set; } = "";
    public DateTime LastSyncedAt { get; set; }
    public string? LastError { get; set; }
}
