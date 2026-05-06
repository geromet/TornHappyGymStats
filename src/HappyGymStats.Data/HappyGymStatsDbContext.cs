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

    public HappyGymStatsDbContext(DbContextOptions<HappyGymStatsDbContext> options)
        : base(options)
    {
    }

    public DbSet<IdentityMapEntity> IdentityMap => Set<IdentityMapEntity>();

    public DbSet<ImportRunEntity> ImportRuns => Set<ImportRunEntity>();

    public DbSet<ModifierProvenanceEntity> ModifierProvenance => Set<ModifierProvenanceEntity>();

    public DbSet<AffiliationEventEntity> AffiliationEvents => Set<AffiliationEventEntity>();

    public DbSet<UserLogEntryEntity> UserLogEntries => Set<UserLogEntryEntity>();

    public DbSet<LogTypeEntity> LogTypes => Set<LogTypeEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityMapEntity>(entity =>
        {
            entity.HasKey(e => e.AnonymousId);
            entity.Property(e => e.AnonymousId).ValueGeneratedNever();
            entity.HasIndex(e => e.KeycloakSub).IsUnique()
                .HasFilter("\"KeycloakSub\" IS NOT NULL");
            entity.Property(e => e.CreatedAtUtc).HasConversion(UtcDateTimeOffsetConverter);
            entity.Property(e => e.ExpiresAtUtc).HasConversion(NullableUtcDateTimeOffsetConverter);
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
    }
}
