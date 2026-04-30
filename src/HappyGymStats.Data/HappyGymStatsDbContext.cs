using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data;

public sealed class HappyGymStatsDbContext : DbContext
{
    public HappyGymStatsDbContext(DbContextOptions<HappyGymStatsDbContext> options)
        : base(options)
    {
    }

    public DbSet<RawUserLogEntity> RawUserLogs => Set<RawUserLogEntity>();

    public DbSet<ImportCheckpointEntity> ImportCheckpoints => Set<ImportCheckpointEntity>();

    public DbSet<ImportRunEntity> ImportRuns => Set<ImportRunEntity>();

    public DbSet<DerivedGymTrainEntity> DerivedGymTrains => Set<DerivedGymTrainEntity>();

    public DbSet<DerivedHappyEventEntity> DerivedHappyEvents => Set<DerivedHappyEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RawUserLogEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.LogId).IsUnique();
            entity.HasIndex(e => e.OccurredAtUtc);
            entity.Property(e => e.LogId).IsRequired();
            entity.Property(e => e.RawJson).IsRequired();
        });

        modelBuilder.Entity<ImportCheckpointEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).IsRequired();
        });

        modelBuilder.Entity<ImportRunEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StartedAtUtc);
            entity.HasIndex(e => e.Outcome);
        });

        modelBuilder.Entity<DerivedGymTrainEntity>(entity =>
        {
            entity.HasKey(e => e.LogId);
            entity.HasIndex(e => e.OccurredAtUtc);
        });

        modelBuilder.Entity<DerivedHappyEventEntity>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.HasIndex(e => e.SourceLogId);
            entity.HasIndex(e => e.OccurredAtUtc);
            entity.HasIndex(e => e.EventType);
            entity.Property(e => e.EventType).IsRequired();
        });
    }
}
