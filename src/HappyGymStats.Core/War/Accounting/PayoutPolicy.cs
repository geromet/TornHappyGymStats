namespace HappyGymStats.Core.War;

/// <summary>
/// How the payout engine treats chain-milestone lumps already embedded in a member's war score.
/// <b>IncludedInRespect</b> credits the full score at the respect rate and pays nothing separately —
/// the milestone reward is paid exactly once, as part of the respect. <b>PaidSeparately</b> removes a
/// detected lump from the respect credit and pays it at <see cref="PayoutRateTable.MilestoneBonusRate"/>
/// instead, so the reward is still paid exactly once, just on its own line. Either way the engine must
/// never pay the same bonus twice — that is the "no double-count" contract of the ledger.
/// </summary>
public enum MilestoneLumpHandling
{
    IncludedInRespect,
    PaidSeparately,
}

/// <summary>
/// The configurable per-kind rates of a payout policy. A zero rate means that kind is not paid.
/// Semantics per kind: <see cref="RespectRatePerPoint"/> is money per respect point on war-hit
/// entries; <see cref="WarHitRate"/> is money per war hit (count); the remaining earned rates are
/// money per event (count) except <see cref="EnergyRatePerPoint"/> which is money per energy point
/// expended. <see cref="MilestoneBonusRate"/> is money per point of a separately-paid milestone lump.
/// </summary>
public sealed record PayoutRateTable(
    decimal RespectRatePerPoint,
    decimal WarHitRate,
    decimal AssistRate,
    decimal OutsideHitRate,
    decimal ChainSaveRate,
    decimal MilestoneBonusRate,
    decimal PushWindowRate,
    decimal RetaliationRate,
    decimal EnergyRatePerPoint);

/// <summary>
/// A versioned payout policy/template. Policies are configuration, never raw events: they live apart
/// from the immutable ledger, so a ledger can be re-run under a different policy without mutating it,
/// and an approved run freezes the exact policy (name, version and rates) that produced it.
/// <see cref="LeadershipCutRate"/> is the fraction of gross earned contribution held back for the
/// faction/reserve. <see cref="ReimburseExpenses"/> controls whether expense lines are paid back to
/// members on top of earned contribution.
/// </summary>
public sealed record PayoutPolicy(
    string Name,
    string Version,
    PayoutRateTable Rates,
    MilestoneLumpHandling LumpHandling,
    decimal LeadershipCutRate,
    bool ReimburseExpenses = true)
{
    public PayoutPolicy WithVersion(string version) => this with { Version = version };
}

/// <summary>
/// Detects a chain-milestone lump inside a member's per-war score using the same residual heuristic as
/// the opponent scout profile: <c>residual = score - attacks * faction median score/attack</c>; when
/// the residual matches a <see cref="ChainEngine.MilestoneBonuses"/> value within tolerance (and the
/// bonus clears the floor), that war is treated as carrying the lump. Sharing the heuristic with
/// scouting matters: the payout engine and the scout board must not disagree about whether a member
/// caught a milestone.
/// </summary>
public static class MilestoneLumpDetector
{
    // Same constants as OpponentProfileEngine: a per-war residual this close (as a fraction of the
    // bonus) to a chain-milestone bonus is treated as that lump, and only bonuses >= 100 are matched
    // (the small 10..80 bonuses sit inside ordinary per-war variance against a faction-median baseline).
    private const decimal LumpResidualToleranceFraction = 0.12m;
    private const int MinDetectableLumpBonus = 100;

    public static int? Detect(decimal score, int attacks, decimal factionMedianScorePerAttack)
    {
        if (attacks <= 0 || score <= 0 || factionMedianScorePerAttack <= 0)
        {
            return null;
        }

        var residual = score - attacks * factionMedianScorePerAttack;

        foreach (var bonus in ChainEngine.MilestoneBonuses)
        {
            if (bonus < MinDetectableLumpBonus)
            {
                continue;
            }

            if (Math.Abs(residual - bonus) <= bonus * LumpResidualToleranceFraction)
            {
                return bonus;
            }
        }

        return null;
    }
}
