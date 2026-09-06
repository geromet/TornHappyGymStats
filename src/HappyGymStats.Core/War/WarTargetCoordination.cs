namespace HappyGymStats.Core.War;

public enum WarTargetClaimMode
{
    Primary,
    Assist,
}

public sealed class WarTargetClaim
{
    public WarTargetClaim(
        Guid id,
        long factionId,
        long warId,
        long targetMemberId,
        long attackerMemberId,
        DateTimeOffset claimedAtUtc,
        DateTimeOffset expiresAtUtc,
        WarTargetClaimMode mode = WarTargetClaimMode.Primary)
    {
        if (id == Guid.Empty) throw new ArgumentException("Claim id must be non-empty.", nameof(id));
        if (factionId <= 0) throw new ArgumentOutOfRangeException(nameof(factionId));
        if (warId <= 0) throw new ArgumentOutOfRangeException(nameof(warId));
        if (targetMemberId <= 0) throw new ArgumentOutOfRangeException(nameof(targetMemberId));
        if (attackerMemberId <= 0) throw new ArgumentOutOfRangeException(nameof(attackerMemberId));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));

        var claimed = claimedAtUtc.ToUniversalTime();
        var expires = expiresAtUtc.ToUniversalTime();
        if (expires <= claimed)
            throw new ArgumentException("Claim expiry must be after claim time.", nameof(expiresAtUtc));

        Id = id;
        FactionId = factionId;
        WarId = warId;
        TargetMemberId = targetMemberId;
        AttackerMemberId = attackerMemberId;
        ClaimedAtUtc = claimed;
        ExpiresAtUtc = expires;
        Mode = mode;
    }

    public Guid Id { get; }
    public long FactionId { get; }
    public long WarId { get; }
    public long TargetMemberId { get; }
    public long AttackerMemberId { get; }
    public DateTimeOffset ClaimedAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public WarTargetClaimMode Mode { get; }

    public bool IsActiveAt(DateTimeOffset instantUtc)
    {
        var instant = instantUtc.ToUniversalTime();
        return ClaimedAtUtc <= instant && instant < ExpiresAtUtc;
    }
}

public sealed class WarTargetReservation
{
    public WarTargetReservation(
        Guid id,
        long factionId,
        long warId,
        long targetMemberId,
        DateTimeOffset activatesAtUtc,
        DateTimeOffset expiresAtUtc,
        long primaryAttackerMemberId,
        IEnumerable<long>? orderedBackupAttackerMemberIds = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Reservation id must be non-empty.", nameof(id));
        if (factionId <= 0) throw new ArgumentOutOfRangeException(nameof(factionId));
        if (warId <= 0) throw new ArgumentOutOfRangeException(nameof(warId));
        if (targetMemberId <= 0) throw new ArgumentOutOfRangeException(nameof(targetMemberId));
        if (primaryAttackerMemberId <= 0) throw new ArgumentOutOfRangeException(nameof(primaryAttackerMemberId));

        var activates = activatesAtUtc.ToUniversalTime();
        var expires = expiresAtUtc.ToUniversalTime();
        if (expires <= activates)
            throw new ArgumentException("Reservation expiry must be after activation.", nameof(expiresAtUtc));

        var backups = (orderedBackupAttackerMemberIds ?? []).ToArray();
        if (backups.Any(memberId => memberId <= 0))
            throw new ArgumentOutOfRangeException(nameof(orderedBackupAttackerMemberIds), "Backup attacker ids must be positive.");
        if (backups.Contains(primaryAttackerMemberId))
            throw new ArgumentException("Primary attacker cannot also be a backup.", nameof(orderedBackupAttackerMemberIds));
        if (backups.Distinct().Count() != backups.Length)
            throw new ArgumentException("Backup attacker ids must be unique.", nameof(orderedBackupAttackerMemberIds));

        Id = id;
        FactionId = factionId;
        WarId = warId;
        TargetMemberId = targetMemberId;
        ActivatesAtUtc = activates;
        ExpiresAtUtc = expires;
        PrimaryAttackerMemberId = primaryAttackerMemberId;
        OrderedBackupAttackerMemberIds = backups;
        CandidateOrder = [primaryAttackerMemberId, .. backups];
    }

    public Guid Id { get; }
    public long FactionId { get; }
    public long WarId { get; }
    public long TargetMemberId { get; }
    public DateTimeOffset ActivatesAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public long PrimaryAttackerMemberId { get; }
    public IReadOnlyList<long> OrderedBackupAttackerMemberIds { get; }
    public IReadOnlyList<long> CandidateOrder { get; }

    public bool IsActiveAt(DateTimeOffset instantUtc)
    {
        var instant = instantUtc.ToUniversalTime();
        return ActivatesAtUtc <= instant && instant < ExpiresAtUtc;
    }
}

public static class WarTargetCoordination
{
    public static IReadOnlyList<WarTargetClaim> ActivePrimaryClaims(
        IEnumerable<WarTargetClaim> claims,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(claims);
        return claims
            .Where(claim => claim.Mode == WarTargetClaimMode.Primary && claim.IsActiveAt(nowUtc))
            .OrderBy(claim => claim.FactionId)
            .ThenBy(claim => claim.WarId)
            .ThenBy(claim => claim.TargetMemberId)
            .ThenBy(claim => claim.AttackerMemberId)
            .ThenBy(claim => claim.Id)
            .ToArray();
    }

    public static void ValidateActivePrimaryUniqueness(
        IEnumerable<WarTargetClaim> claims,
        DateTimeOffset nowUtc)
    {
        var active = ActivePrimaryClaims(claims, nowUtc);

        if (active.GroupBy(claim => (claim.FactionId, claim.WarId, claim.TargetMemberId)).Any(group => group.Count() > 1))
            throw new InvalidOperationException("A target cannot have more than one live primary claim in the same faction war.");

        if (active.GroupBy(claim => (claim.FactionId, claim.WarId, claim.AttackerMemberId)).Any(group => group.Count() > 1))
            throw new InvalidOperationException("An attacker cannot hold more than one live primary claim in the same faction war.");
    }

    public static bool CanCreatePrimaryClaim(
        long factionId,
        long warId,
        long targetMemberId,
        long attackerMemberId,
        IEnumerable<WarTargetClaim> claims,
        DateTimeOffset nowUtc)
    {
        if (factionId <= 0) throw new ArgumentOutOfRangeException(nameof(factionId));
        if (warId <= 0) throw new ArgumentOutOfRangeException(nameof(warId));
        if (targetMemberId <= 0) throw new ArgumentOutOfRangeException(nameof(targetMemberId));
        if (attackerMemberId <= 0) throw new ArgumentOutOfRangeException(nameof(attackerMemberId));

        var active = ActivePrimaryClaims(claims, nowUtc)
            .Where(claim => claim.FactionId == factionId && claim.WarId == warId)
            .ToArray();
        return active.All(claim =>
            claim.TargetMemberId != targetMemberId
            && claim.AttackerMemberId != attackerMemberId);
    }

    public static long? SelectReservationCandidate(
        WarTargetReservation reservation,
        IEnumerable<WarTargetClaim> claims,
        IReadOnlySet<long> unavailableAttackerMemberIds,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(unavailableAttackerMemberIds);

        if (!reservation.IsActiveAt(nowUtc))
            return null;

        var active = ActivePrimaryClaims(claims, nowUtc)
            .Where(claim => claim.FactionId == reservation.FactionId && claim.WarId == reservation.WarId)
            .ToArray();

        if (active.Any(claim => claim.TargetMemberId == reservation.TargetMemberId))
            return null;

        foreach (var candidate in reservation.CandidateOrder)
        {
            if (unavailableAttackerMemberIds.Contains(candidate))
                continue;
            if (active.Any(claim => claim.AttackerMemberId == candidate))
                continue;
            return candidate;
        }

        return null;
    }
}
