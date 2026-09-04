using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HappyGymStats.Data;

public sealed class HappyGymStatsDbContext : DbContext, IUnitOfWork
{
    private static readonly ValueConverter<DateTimeOffset, DateTime> UtcDateTimeOffsetConverter = new(
        value => value.UtcDateTime,
        value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

    private static readonly ValueConverter<DateTimeOffset?, DateTime?> NullableUtcDateTimeOffsetConverter = new(
        value => value.HasValue ? value.Value.UtcDateTime : null,
        value => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null);

    public HappyGymStatsDbContext(DbContextOptions<HappyGymStatsDbContext> options) : base(options)
    {
    }

    public DbSet<AppSettingEntity> AppSettings => Set<AppSettingEntity>();
    public DbSet<IdentityMapEntity> IdentityMap => Set<IdentityMapEntity>();
    public DbSet<ConsentRecordEntity> ConsentRecords => Set<ConsentRecordEntity>();
    public DbSet<ImportRunEntity> ImportRuns => Set<ImportRunEntity>();
    public DbSet<ModifierProvenanceEntity> ModifierProvenance => Set<ModifierProvenanceEntity>();
    public DbSet<AffiliationEventEntity> AffiliationEvents => Set<AffiliationEventEntity>();
    public DbSet<FactionIdMapEntity> FactionIdMap => Set<FactionIdMapEntity>();
    public DbSet<FactionMembershipEntity> FactionMembership => Set<FactionMembershipEntity>();
    public DbSet<UserLogEntryEntity> UserLogEntries => Set<UserLogEntryEntity>();
    public DbSet<LogTypeEntity> LogTypes => Set<LogTypeEntity>();
    public DbSet<RankedWarHistoryEntity> RankedWarHistory => Set<RankedWarHistoryEntity>();
    public DbSet<RankedWarReportMemberEntity> RankedWarReportMembers => Set<RankedWarReportMemberEntity>();
    public DbSet<WarCurrentEntity> WarCurrent => Set<WarCurrentEntity>();
    public DbSet<WarRosterSnapshotEntity> WarRosterSnapshots => Set<WarRosterSnapshotEntity>();
    public DbSet<WarScoreSampleEntity> WarScoreSamples => Set<WarScoreSampleEntity>();
    public DbSet<WarPollerHeartbeatEntity> WarPollerHeartbeats => Set<WarPollerHeartbeatEntity>();
    public DbSet<RankedWarHistoryBackfillStateEntity> RankedWarHistoryBackfillState => Set<RankedWarHistoryBackfillStateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSettingEntity>(entity =>
        {
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasMaxLength(200).ValueGeneratedNever();
            entity.Property(e => e.Value).HasMaxLength(2000);
            entity.Property(e => e.UpdatedBy).HasMaxLength(200);
            entity.Property(e => e.UpdatedAtUtc).HasConversion(UtcDateTimeOffsetConverter);
        });

        modelBuilder.Entity<IdentityMapEntity>(entity =>
        {
            entity.HasKey(e => e.AnonymousId);
            entity.Property(e => e.AnonymousId).ValueGeneratedNever();
            entity.HasIndex(e => e.KeycloakSub).IsUnique()
                .HasFilter("\"KeycloakSub\" IS NOT NULL");
            entity.Property(e => e.CreatedAtUtc).HasConversion(UtcDateTimeOffsetConverter);
            entity.Property(e => e.ExpiresAtUtc).HasConversion(NullableUtcDateTimeOffsetConverter);
            entity.Property(e => e.PublicKey).HasColumnType("bytea");
            entity.Property(e => e.EncryptedTornPlayerId).HasColumnType("bytea");
        });

        modelBuilder.Entity<ConsentRecordEntity>(entity =>
        {
            entity.ToTable("ConsentRecords");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DocumentVersion).IsRequired().HasMaxLength(32);
            entity.Property(e => e.Purpose).IsRequired().HasMaxLength(64);
            entity.Property(e => e.AcceptedAtUtc).HasConversion(UtcDateTimeOffsetConverter);
            entity.Property(e => e.RevokedAtUtc).HasConversion(NullableUtcDateTimeOffsetConverter);
            entity.HasIndex(e => new { e.AnonymousId, e.Purpose, e.DocumentVersion });
            entity.HasIndex(e => new { e.AnonymousId, e.Purpose, e.RevokedAtUtc });
        });

        modelBuilder.Entity<ImportRunEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.AnonymousId, e.StartedAtUtc });
            entity.HasIndex(e => e.Outcome);
            entity.Property(e => e.StartedAtUtc).HasConversion(UtcDateTimeOffsetConverter);
            entity.Property(e => e.CompletedAtUtc).HasConversion(NullableUtcDateTimeOffsetConverter);
        });

        modelBuilder.Entity<ModifierProvenanceEntity>(entity =>
        {
            entity.HasKey(e => new { e.AnonymousId, e.LogEntryId, e.Scope });
            entity.HasIndex(e => new { e.AnonymousId, e.VerificationStatus });
            entity.Property(e => e.LogEntryId).IsRequired();
        });

        modelBuilder.Entity<AffiliationEventEntity>(entity =>
        {
            entity.HasKey(e => new { e.AnonymousId, e.SourceLogEntryId });
            entity.HasIndex(e => new { e.AnonymousId, e.Scope, e.AffiliationId });
            entity.Property(e => e.SourceLogEntryId).IsRequired();
            entity.Property(e => e.Scope).HasConversion<int>();
            entity.Property(e => e.EncryptedAffiliationId).HasColumnType("bytea");
        });

        modelBuilder.Entity<FactionIdMapEntity>(entity =>
        {
            entity.HasKey(e => e.AffiliationId);
            entity.Property(e => e.AffiliationId).ValueGeneratedNever();
            entity.HasIndex(e => e.FactionAnonymousId).IsUnique();
            entity.Property(e => e.Scope).HasConversion<int>();
        });

        modelBuilder.Entity<FactionMembershipEntity>(entity =>
        {
            entity.HasKey(e => new { e.FactionAnonymousId, e.MemberAnonymousId });
            entity.HasIndex(e => e.MemberAnonymousId);
        });

        modelBuilder.Entity<UserLogEntryEntity>(entity =>
        {
            entity.HasKey(e => new { e.AnonymousId, e.LogEntryId });
            entity.HasIndex(e => new { e.AnonymousId, e.OccurredAtUtc });
            entity.HasIndex(e => new { e.AnonymousId, e.LogTypeId });
            entity.Property(e => e.LogEntryId).IsRequired();
            entity.Property(e => e.OccurredAtUtc).HasConversion(UtcDateTimeOffsetConverter);
        });

        modelBuilder.Entity<LogTypeEntity>(entity =>
        {
            entity.HasKey(e => e.LogTypeId);
            entity.Property(e => e.LogTypeId).ValueGeneratedNever();
            entity.Property(e => e.LogTypeTitle).IsRequired();
        });

        modelBuilder.Entity<RankedWarHistoryEntity>(entity =>
        {
            entity.ToTable("RankedWarHistory");
            entity.HasKey(e => e.WarId);
            entity.Property(e => e.WarId).ValueGeneratedNever();
            entity.Property(e => e.FactionName).HasMaxLength(128);
            entity.Property(e => e.OpponentFactionName).HasMaxLength(128);
            entity.Property(e => e.Status).HasMaxLength(64);
            entity.Property(e => e.StartedAtUtc).HasConversion(UtcDateTimeOffsetConverter);
            entity.Property(e => e.EndedAtUtc).HasConversion(NullableUtcDateTimeOffsetConverter);
            entity.Property(e => e.CapturedAtUtc).HasConversion(UtcDateTimeOffsetConverter);
            entity.Property(e => e.IngestedAtUtc).HasConversion(UtcDateTimeOffsetConverter);
            entity.Property(e => e.ReportCapturedAtUtc).HasConversion(NullableUtcDateTimeOffsetConverter);
            entity.Property(e => e.ReportIngestedAtUtc).HasConversion(NullableUtcDateTimeOffsetConverter);
            entity.HasIndex(e => new { e.FactionId, e.StartedAtUtc });
            entity.HasIndex(e => new { e.OpponentFactionId, e.StartedAtUtc });
            entity.HasIndex(e => e.EndedAtUtc);
        });

        modelBuilder.Entity<RankedWarReportMemberEntity>(entity =>
        {
            entity.ToTable("RankedWarReportMembers");
            entity.HasKey(e => new { e.WarId, e.FactionId, e.MemberId });
            entity.Property(e => e.FactionName).HasMaxLength(128);
            entity.Property(e => e.MemberName).HasMaxLength(128);
            entity.Property(e => e.StatusState).HasMaxLength(64);
            entity.Property(e => e.StatusUntilUtc).HasConversion(NullableUtcDateTimeOffsetConverter);
            entity.Property(e => e.CapturedAtUtc).HasConversion(UtcDateTimeOffsetConverter);
            entity.Property(e => e.IngestedAtUtc).HasConversion(UtcDateTimeOffsetConverter);
            entity.HasIndex(e => new { e.WarId, e.FactionId });
            entity.HasIndex(e => new { e.FactionId, e.MemberId });
            entity.HasIndex(e => e.MemberId);
        });

        modelBuilder.Entity<WarCurrentEntity>(entity =>
        {
            entity.ToTable("WarCurrent");
            entity.HasKey(e => e.ScopeKey);
            entity.Property(e => e.ScopeKey).HasMaxLength(64);
            entity.Property(e => e.FactionName).HasMaxLength(128);
            entity.Property(e => e.OpponentFactionName).HasMaxLength(128);
            entity.Property(e => e.StartedAtUtc).HasConversion(NullableUtcDateTimeOffsetConverter);
            entity.Property(e => e.EndsAtUtc).HasConversion(NullableUtcDateTimeOffsetConverter);
            entity.Property(e => e.ObservedAtUtc).HasConversion(UtcDateTimeOffsetConverter);
            entity.HasIndex(e => e.WarId).IsUnique()
                .HasFilter("\"WarId\" IS NOT NULL");
        });

        modelBuilder.Entity<WarRosterSnapshotEntity>(entity =>
        {
            entity.ToTable("WarRosterSnapshots");
            entity.HasKey(e => new { e.WarId, e.FactionId, e.MemberId });
            entity.Property(e => e.FactionName).HasMaxLength(128);
            entity.Property(e => e.MemberName).HasMaxLength(128);
            entity.Property(e => e.StatusState).HasMaxLength(64);
            entity.Property(e => e.StatusUntilUtc).HasConversion(NullableUtcDateTimeOffsetConverter);
            entity.Property(e => e.CapturedAtUtc).HasConversion(UtcDateTimeOffsetConverter);
            entity.HasIndex(e => new { e.WarId, e.CapturedAtUtc });
        });

        modelBuilder.Entity<WarScoreSampleEntity>(entity =>
        {
            entity.ToTable("WarScoreSamples");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FactionName).HasMaxLength(128);
            entity.Property(e => e.OpponentFactionName).HasMaxLength(128);
            entity.Property(e => e.SampledAtUtc).HasConversion(UtcDateTimeOffsetConverter);
            entity.Property(e => e.FactionChainLapsesAtUtc).HasConversion(NullableUtcDateTimeOffsetConverter);
            entity.HasIndex(e => new { e.WarId, e.SampledAtUtc });
        });

        modelBuilder.Entity<WarPollerHeartbeatEntity>(entity =>
        {
            entity.ToTable("WarPollerHeartbeats");
            entity.HasKey(e => e.ScopeKey);
            entity.Property(e => e.ScopeKey).HasMaxLength(64);
            entity.Property(e => e.Phase).IsRequired().HasMaxLength(64);
            entity.Property(e => e.LastError).HasMaxLength(1024);
            entity.Property(e => e.UpdatedAtUtc).HasConversion(UtcDateTimeOffsetConverter);
            entity.Property(e => e.PollStartedAtUtc).HasConversion(NullableUtcDateTimeOffsetConverter);
            entity.Property(e => e.PollCompletedAtUtc).HasConversion(NullableUtcDateTimeOffsetConverter);
            entity.Property(e => e.StaleAfterUtc).HasConversion(NullableUtcDateTimeOffsetConverter);
            entity.HasIndex(e => e.ActiveWarId);
            entity.HasIndex(e => new { e.Phase, e.UpdatedAtUtc });
        });

        modelBuilder.Entity<RankedWarHistoryBackfillStateEntity>(entity =>
        {
            entity.ToTable("RankedWarHistoryBackfillState");
            entity.HasKey(e => e.ScopeKey);
            entity.Property(e => e.ScopeKey).HasMaxLength(64);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(32);
            entity.Property(e => e.Phase).HasMaxLength(64);
            entity.Property(e => e.NextHistoryPageUrl).HasMaxLength(2048);
            entity.Property(e => e.LastFailureCategory).HasMaxLength(32);
            entity.Property(e => e.LastErrorMessage).HasMaxLength(1024);
            entity.Property(e => e.LastSuccessAtUtc).HasConversion(NullableUtcDateTimeOffsetConverter);
            entity.Property(e => e.LastFailureAtUtc).HasConversion(NullableUtcDateTimeOffsetConverter);
            entity.Property(e => e.NextRetryAtUtc).HasConversion(NullableUtcDateTimeOffsetConverter);
            entity.Property(e => e.CreatedAtUtc).HasConversion(UtcDateTimeOffsetConverter);
            entity.Property(e => e.UpdatedAtUtc).HasConversion(UtcDateTimeOffsetConverter);
            entity.HasIndex(e => e.NextRetryAtUtc);
        });
    }
}
