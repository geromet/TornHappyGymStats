using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.War;

internal static class WarMemberDerivationCalculator
{
    internal static HashSet<long> BuildIdleAttackerSet(
        IReadOnlyCollection<WarRosterSnapshotEntity> rosterRows,
        IReadOnlyCollection<long>? explicitIdleAttackerIds,
        ICollection<string> warnings)
    {
        var rosterMemberIds = rosterRows.Select(row => row.MemberId).ToHashSet();
        var idleIds = rosterRows
            .Where(row => string.Equals(row.StatusState, "idle", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.MemberId)
            .ToHashSet();

        if (explicitIdleAttackerIds is null)
        {
            return idleIds;
        }

        foreach (var memberId in explicitIdleAttackerIds)
        {
            idleIds.Add(memberId);
            if (!rosterMemberIds.Contains(memberId))
            {
                warnings.Add($"Idle attacker id {memberId} was not present in the roster snapshot.");
            }
        }

        return idleIds;
    }

    internal static WarDerivedMemberState DeriveMemberState(
        WarRosterSnapshotEntity row,
        DateTimeOffset asOfUtc,
        bool isIdleAttacker)
    {
        var normalizedState = row.StatusState?.Trim().ToLowerInvariant();
        var untilUtc = row.StatusUntilUtc?.ToUniversalTime();
        var hospitalCountdown = 0;
        var availability = normalizedState switch
        {
            null or "" or "okay" or "idle" => WarMemberAvailabilityKind.Available,
            "hospital" when untilUtc.HasValue && untilUtc.Value > asOfUtc => WarMemberAvailabilityKind.Hospitalized,
            "hospital" => WarMemberAvailabilityKind.Available,
            "travel" or "jail" or "federal" or "abroad" => WarMemberAvailabilityKind.Unavailable,
            _ => WarMemberAvailabilityKind.Unknown,
        };

        if (string.Equals(normalizedState, "hospital", StringComparison.Ordinal)
            && untilUtc.HasValue
            && untilUtc.Value > asOfUtc)
        {
            hospitalCountdown = (int)Math.Ceiling((untilUtc.Value - asOfUtc).TotalSeconds);
        }

        return new WarDerivedMemberState
        {
            MemberId = row.MemberId,
            MemberName = row.MemberName,
            Score = row.Score,
            Chain = row.Chain,
            Attacks = row.Attacks,
            StatusState = row.StatusState,
            StatusUntilUtc = row.StatusUntilUtc,
            Availability = availability,
            HospitalCountdownSeconds = Math.Max(0, hospitalCountdown),
            IsIdleAttacker = isIdleAttacker,
            CapturedAtUtc = row.CapturedAtUtc,
        };
    }

    internal static WarMemberCoverage CalculateCoverage(IReadOnlyCollection<WarDerivedMemberState> members)
    {
        var available = members.Count(member => member.Availability == WarMemberAvailabilityKind.Available);
        var idleAvailable = members.Count(member =>
            member.IsIdleAttacker && member.Availability == WarMemberAvailabilityKind.Available);

        return new WarMemberCoverage(
            available,
            members.Count(member => member.Availability == WarMemberAvailabilityKind.Hospitalized),
            members.Count(member => member.Availability is WarMemberAvailabilityKind.Unavailable or WarMemberAvailabilityKind.Unknown),
            available == 0
                ? 1m
                : decimal.Round((available - idleAvailable) / (decimal)available, 4, MidpointRounding.AwayFromZero));
    }
}

internal readonly record struct WarMemberCoverage(
    int AvailableMemberCount,
    int HospitalizedMemberCount,
    int UnavailableMemberCount,
    decimal CoverageRatio);
