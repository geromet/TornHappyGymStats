namespace HappyGymStats.Core.War;

internal static class WarHoleCalculator
{
    internal static IReadOnlyList<WarHoleRecord> DeriveHoles(
        IReadOnlyList<WarDerivedFactionState> factions,
        IReadOnlyDictionary<long, List<WarDerivedMemberState>> memberStateByFactionId)
    {
        if (factions.Count == 0)
        {
            return [];
        }

        var holes = new List<WarHoleRecord>();
        foreach (var faction in factions)
        {
            var opponent = factions.FirstOrDefault(candidate => candidate.FactionId != faction.FactionId);
            var members = memberStateByFactionId[faction.FactionId];

            foreach (var member in members.Where(member => member.IsIdleAttacker))
            {
                holes.Add(new WarHoleRecord
                {
                    Kind = WarHoleKind.IdleAttacker,
                    Severity = ResolveIdleSeverity(member),
                    FactionId = faction.FactionId,
                    FactionName = faction.FactionName,
                    OpponentFactionId = opponent?.FactionId,
                    MemberId = member.MemberId,
                    MemberName = member.MemberName,
                    Reason = member.Availability == WarMemberAvailabilityKind.Available
                        ? "Available attacker is marked idle."
                        : "Idle attacker feed references a member who is not currently available.",
                });
            }

            if (opponent is null)
            {
                continue;
            }

            // An open slot is a first-class board object per data/V2/handoff/04: an attackable
            // opponent target. "Who is free" and "who is available to hit" are the same question,
            // so this does NOT depend on this faction having idle attackers, and a target being
            // idle does not disqualify it - an idle enemy is a prime target. A hospitalised enemy
            // is a slot that regenerates at status.until, not a hole; that is already handled here
            // by requiring Availability == Available (hospital -> Hospitalized).
            // KNOWN INCOMPLETE: the handoff's "with no live claim against them" cannot be applied
            // until M010 adds ClaimTarget - every attackable target is reported until then.
            foreach (var target in opponent.Members.Where(member => member.Availability == WarMemberAvailabilityKind.Available))
            {
                holes.Add(new WarHoleRecord
                {
                    Kind = WarHoleKind.OpenTarget,
                    Severity = WarHoleSeverity.Medium,
                    FactionId = faction.FactionId,
                    FactionName = faction.FactionName,
                    OpponentFactionId = opponent.FactionId,
                    MemberId = target.MemberId,
                    MemberName = target.MemberName,
                    Reason = target.IsIdleAttacker
                        ? $"Opponent {target.MemberName} is attackable and idle."
                        : $"Opponent {target.MemberName} is attackable with no claim recorded.",
                });
            }
        }

        return holes;
    }

    private static WarHoleSeverity ResolveIdleSeverity(WarDerivedMemberState member)
        => member.Availability switch
        {
            WarMemberAvailabilityKind.Available => WarHoleSeverity.Critical,
            WarMemberAvailabilityKind.Hospitalized => WarHoleSeverity.High,
            WarMemberAvailabilityKind.Unavailable => WarHoleSeverity.High,
            _ => WarHoleSeverity.Medium,
        };
}
