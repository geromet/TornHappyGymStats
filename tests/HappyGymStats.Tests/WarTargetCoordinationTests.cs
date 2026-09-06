using HappyGymStats.Core.War;

namespace HappyGymStats.Tests;

public sealed class WarTargetCoordinationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Claim_uses_half_open_utc_ttl()
    {
        var claim = Claim(11, 21, Now, Now.AddMinutes(5));

        Assert.True(claim.IsActiveAt(Now));
        Assert.True(claim.IsActiveAt(Now.AddMinutes(5).AddTicks(-1)));
        Assert.False(claim.IsActiveAt(Now.AddMinutes(5)));
    }

    [Fact]
    public void Claim_normalizes_offsets_to_utc()
    {
        var offset = TimeSpan.FromHours(2);
        var claim = Claim(
            11,
            21,
            new DateTimeOffset(2026, 9, 6, 12, 0, 0, offset),
            new DateTimeOffset(2026, 9, 6, 12, 5, 0, offset));

        Assert.Equal(Now, claim.ClaimedAtUtc);
        Assert.Equal(Now.AddMinutes(5), claim.ExpiresAtUtc);
    }

    [Fact]
    public void Duplicate_live_primary_target_is_rejected()
    {
        var claims = new[]
        {
            Claim(11, 21, Now.AddMinutes(-1), Now.AddMinutes(5)),
            Claim(12, 21, Now.AddMinutes(-1), Now.AddMinutes(5)),
        };

        Assert.Throws<InvalidOperationException>(() =>
            WarTargetCoordination.ValidateActivePrimaryUniqueness(claims, Now));
    }

    [Fact]
    public void Duplicate_live_primary_attacker_is_rejected()
    {
        var claims = new[]
        {
            Claim(11, 21, Now.AddMinutes(-1), Now.AddMinutes(5)),
            Claim(11, 22, Now.AddMinutes(-1), Now.AddMinutes(5)),
        };

        Assert.Throws<InvalidOperationException>(() =>
            WarTargetCoordination.ValidateActivePrimaryUniqueness(claims, Now));
    }

    [Fact]
    public void Expired_claim_does_not_block_replacement()
    {
        var expired = Claim(11, 21, Now.AddMinutes(-10), Now);

        Assert.True(WarTargetCoordination.CanCreatePrimaryClaim(
            100, 200, 21, 12, [expired], Now));
    }

    [Fact]
    public void Assist_does_not_consume_primary_uniqueness_slot()
    {
        var primary = Claim(11, 21, Now.AddMinutes(-1), Now.AddMinutes(5));
        var assist = new WarTargetClaim(
            Guid.NewGuid(), 100, 200, 21, 12,
            Now.AddMinutes(-1), Now.AddMinutes(5), WarTargetClaimMode.Assist);

        WarTargetCoordination.ValidateActivePrimaryUniqueness([primary, assist], Now);
    }

    [Fact]
    public void Reservation_promotes_backups_in_declared_order()
    {
        var reservation = Reservation(41, [42, 43]);

        Assert.Equal(42, WarTargetCoordination.SelectReservationCandidate(
            reservation,
            claims: [],
            unavailableAttackerMemberIds: new HashSet<long> { 41 },
            Now));

        Assert.Equal(43, WarTargetCoordination.SelectReservationCandidate(
            reservation,
            claims: [],
            unavailableAttackerMemberIds: new HashSet<long> { 41, 42 },
            Now));
    }

    [Fact]
    public void Active_claim_on_candidate_or_target_blocks_promotion()
    {
        var reservation = Reservation(41, [42]);
        var attackerBusy = Claim(41, 99, Now.AddMinutes(-1), Now.AddMinutes(5));
        var targetBusy = Claim(55, 21, Now.AddMinutes(-1), Now.AddMinutes(5));

        Assert.Equal(42, WarTargetCoordination.SelectReservationCandidate(
            reservation, [attackerBusy], new HashSet<long>(), Now));
        Assert.Null(WarTargetCoordination.SelectReservationCandidate(
            reservation, [targetBusy], new HashSet<long>(), Now));
    }

    [Fact]
    public void Scope_is_faction_and_war_specific()
    {
        var otherWar = new WarTargetClaim(
            Guid.NewGuid(), 100, 201, 21, 11,
            Now.AddMinutes(-1), Now.AddMinutes(5));
        var otherFaction = new WarTargetClaim(
            Guid.NewGuid(), 101, 200, 21, 12,
            Now.AddMinutes(-1), Now.AddMinutes(5));

        WarTargetCoordination.ValidateActivePrimaryUniqueness([otherWar, otherFaction], Now);
        Assert.True(WarTargetCoordination.CanCreatePrimaryClaim(
            100, 200, 21, 11, [otherWar, otherFaction], Now));
    }

    [Fact]
    public void Reservation_rejects_duplicate_candidates_and_inactive_windows()
    {
        Assert.Throws<ArgumentException>(() => Reservation(41, [42, 42]));
        Assert.Throws<ArgumentException>(() => Reservation(41, [41]));

        var future = new WarTargetReservation(
            Guid.NewGuid(), 100, 200, 21,
            Now.AddMinutes(1), Now.AddMinutes(10), 41, [42]);
        Assert.Null(WarTargetCoordination.SelectReservationCandidate(
            future, [], new HashSet<long>(), Now));
    }

    private static WarTargetClaim Claim(
        long attackerMemberId,
        long targetMemberId,
        DateTimeOffset claimedAtUtc,
        DateTimeOffset expiresAtUtc) =>
        new(Guid.NewGuid(), 100, 200, targetMemberId, attackerMemberId, claimedAtUtc, expiresAtUtc);

    private static WarTargetReservation Reservation(long primary, IEnumerable<long> backups) =>
        new(Guid.NewGuid(), 100, 200, 21, Now.AddMinutes(-1), Now.AddMinutes(10), primary, backups);
}
