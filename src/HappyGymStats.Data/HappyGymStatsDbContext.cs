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

    public DbSet<ModifierProvenanceEntity> ModifierProvenance => Set<ModifierProvenanceEntity>();

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

        modelBuilder.Entity<ModifierProvenanceEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Scope, e.ValidFromUtc, e.ValidToUtc });
            entity.HasIndex(e => new { e.DerivedGymTrainLogId, e.Scope }).IsUnique();
            entity.HasIndex(e => e.VerificationStatus);
            entity.Property(e => e.DerivedGymTrainLogId).IsRequired();
            entity.Property(e => e.Scope).IsRequired();
            entity.Property(e => e.VerificationStatus).IsRequired();
            entity.Property(e => e.VerificationReasonCode).IsRequired();
            entity.Property(e => e.ValidFromUtc).HasConversion(UtcDateTimeOffsetConverter);
            entity.Property(e => e.ValidToUtc).HasConversion(NullableUtcDateTimeOffsetConverter);

            entity.ToTable(tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("CK_ModifierProvenance_Scope", "Scope IN ('personal', 'faction', 'company')");
                tableBuilder.HasCheckConstraint("CK_ModifierProvenance_VerificationStatus", "VerificationStatus IN ('verified', 'unresolved', 'unavailable')");
                tableBuilder.HasCheckConstraint("CK_ModifierProvenance_SubjectRequired", "Scope <> 'personal' OR (SubjectId IS NOT NULL AND length(trim(SubjectId)) > 0)");
                tableBuilder.HasCheckConstraint("CK_ModifierProvenance_FactionRequired", "Scope <> 'faction' OR (FactionId IS NOT NULL AND length(trim(FactionId)) > 0)");
                tableBuilder.HasCheckConstraint("CK_ModifierProvenance_CompanyRequired", "Scope <> 'company' OR (CompanyId IS NOT NULL AND length(trim(CompanyId)) > 0)");
            });

            entity.HasOne(e => e.DerivedGymTrain)
                .WithMany()
                .HasForeignKey(e => e.DerivedGymTrainLogId)
                .HasPrincipalKey(e => e.LogId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
