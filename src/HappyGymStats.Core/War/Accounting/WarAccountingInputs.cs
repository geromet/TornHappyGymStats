namespace HappyGymStats.Core.War;

/// <summary>
/// A supplemental raw event that the ranked-war report does not carry (assists, outside/filler chain
/// hits, chain saves, retaliation hits, energy expenditure, faction-provided Xanax/points/meds,
/// bounties, revive-contract costs, other expenses). Money-bearing kinds must supply
/// <see cref="Amount"/>; respect-bearing earned kinds supply <see cref="Respect"/> and/or
/// <see cref="Count"/>.
/// </summary>
public sealed record WarLedgerSupplement(
    long MemberId,
    string MemberName,
    WarLedgerEntryKind Kind,
    decimal? Amount = null,
    decimal? Respect = null,
    int? Count = null,
    DateTimeOffset? OccurredAtUtc = null,
    string? SourceReference = null);

/// <summary>
/// A signed correction to a member's payout. Per the accounting principles, a manual adjustment is
/// not anonymous: <see cref="ActorId"/>, <see cref="ActorName"/>, <see cref="TimestampUtc"/> and
/// <see cref="Reason"/> are all required.
/// </summary>
public sealed record ManualAdjustmentInput(
    long MemberId,
    string MemberName,
    decimal Amount,
    string ActorId,
    string ActorName,
    DateTimeOffset TimestampUtc,
    string Reason);

/// <summary>
/// One cache item held in the faction vault. <see cref="MemberId"/> null means the cache belongs to
/// the faction and its value is liquidated into the payout pool; a member id means the faction buys
/// that member's cache out and pays <see cref="Quantity"/> x <see cref="UnitValue"/> to the member.
/// </summary>
public sealed record CacheSettlementInput(
    long? MemberId,
    string? MemberName,
    string ItemName,
    int Quantity,
    decimal UnitValue)
{
    public decimal TotalValue => Quantity * UnitValue;
}

/// <summary>Which direction a termed-war settlement money movement runs.</summary>
public enum TermedSettlementDirection
{
    Received,
    Paid,
}

/// <summary>
/// A negotiated settlement between factions for a termed war. <see cref="Direction"/> decides whether
/// the amount flows into the pool (received) or out of it (paid to the opposing faction).
/// </summary>
public sealed record TermedSettlementInput(
    decimal Amount,
    TermedSettlementDirection Direction,
    string? Note = null);
