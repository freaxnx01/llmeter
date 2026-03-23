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
