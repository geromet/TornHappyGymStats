using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HappyGymStats.Data;

public sealed class HappyGymStatsDbContext : DbContext
{
    private static readonly ValueConverter<DateTimeOffset, DateTime> UtcDateTimeOffsetConverter = new(
        value => value.UtcDateTime,
        value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

    private static readonly ValueConverter<DateTimeOffset?, DateTime?> NullableUtcDateTimeOffsetConverter = new(
        value => value.HasValue ? value.Value.UtcDateTime : null,
        value => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null);

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
            entity.Property(e => e.OccurredAtUtc).HasConversion(UtcDateTimeOffsetConverter);
            entity.Property(e => e.RawJson).IsRequired();
        });

        modelBuilder.Entity<ImportCheckpointEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.LastLogTimestamp).HasConversion(NullableUtcDateTimeOffsetConverter);
            entity.Property(e => e.LastRunStartedAt).HasConversion(NullableUtcDateTimeOffsetConverter);
            entity.Property(e => e.LastRunCompletedAt).HasConversion(NullableUtcDateTimeOffsetConverter);
            entity.Property(e => e.LastErrorAt).HasConversion(NullableUtcDateTimeOffsetConverter);
        });

        modelBuilder.Entity<ImportRunEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StartedAtUtc);
            entity.HasIndex(e => e.Outcome);
            entity.Property(e => e.StartedAtUtc).HasConversion(UtcDateTimeOffsetConverter);
            entity.Property(e => e.CompletedAtUtc).HasConversion(NullableUtcDateTimeOffsetConverter);
        });

        modelBuilder.Entity<DerivedGymTrainEntity>(entity =>
        {
            entity.HasKey(e => e.LogId);
            entity.HasIndex(e => e.OccurredAtUtc);
            entity.Property(e => e.OccurredAtUtc).HasConversion(UtcDateTimeOffsetConverter);
        });

        modelBuilder.Entity<DerivedHappyEventEntity>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.HasIndex(e => e.SourceLogId);
            entity.HasIndex(e => e.OccurredAtUtc);
            entity.HasIndex(e => e.EventType);
            entity.HasIndex(e => e.SortOrder);
            entity.Property(e => e.EventType).IsRequired();
            entity.Property(e => e.OccurredAtUtc).HasConversion(UtcDateTimeOffsetConverter);
        });
    }
}
