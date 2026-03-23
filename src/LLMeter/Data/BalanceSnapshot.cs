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
