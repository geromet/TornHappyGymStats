namespace HappyGymStats.Core.War;

public sealed class ChainAttackSlot
{
    public ChainAttackSlot(
        Guid id,
        DateTimeOffset startsAtUtc,
        long primaryMemberId,
        IEnumerable<long>? orderedBackupMemberIds = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Attack slot id must be non-empty.", nameof(id));
        if (primaryMemberId <= 0) throw new ArgumentOutOfRangeException(nameof(primaryMemberId), "Primary member id must be positive.");

        var backups = (orderedBackupMemberIds ?? [])
            .ToArray();
        if (backups.Any(memberId => memberId <= 0))
            throw new ArgumentOutOfRangeException(nameof(orderedBackupMemberIds), "Backup member ids must be positive.");
        if (backups.Contains(primaryMemberId))
            throw new ArgumentException("Primary member cannot also be a backup.", nameof(orderedBackupMemberIds));
        if (backups.Distinct().Count() != backups.Length)
            throw new ArgumentException("Backup member ids must be unique and ordered.", nameof(orderedBackupMemberIds));

        Id = id;
        StartsAtUtc = startsAtUtc.ToUniversalTime();
        PrimaryMemberId = primaryMemberId;
        OrderedBackupMemberIds = backups;
    }

    public Guid Id { get; }
    public DateTimeOffset StartsAtUtc { get; }
    public long PrimaryMemberId { get; }
    public IReadOnlyList<long> OrderedBackupMemberIds { get; }

    public IReadOnlyList<long> CandidateOrder
        => [PrimaryMemberId, .. OrderedBackupMemberIds];

    /// <summary>
    /// Selects the first candidate not rejected by the caller-supplied availability set.
    /// Reservation/claim ownership stays outside this type so #83 remains the single authority.
    /// </summary>
    public long? SelectCandidate(IReadOnlySet<long> unavailableOrReservedMemberIds)
    {
        ArgumentNullException.ThrowIfNull(unavailableOrReservedMemberIds);

        foreach (var memberId in CandidateOrder)
        {
            if (!unavailableOrReservedMemberIds.Contains(memberId))
                return memberId;
        }

        return null;
    }
}
