using HappyGymStats.Core.Models;
using HappyGymStats.Core.War;

namespace HappyGymStats.Api.Models;

/// <summary>
/// Maps Core war derived state to the shared war DTOs (<see cref="HappyGymStats.Core.Models"/>).
/// The DTO record definitions are owned by the Contracts assembly; only the mapping lives here.
/// </summary>
public static class WarDtoMapper
{
    public static WarStateDto ToStateDto(this WarDerivedState state)
    {
        var factions = state.Factions.Select(ToFactionDto).ToArray();
        var memberCount = factions.Sum(f => f.Members.Count);
        var status = ComputeStatus(state);

        return new WarStateDto(
            Status: status,
            IsReady: status == "ok",
            WarId: state.WarId,
            AsOfUtc: state.AsOfUtc,
            HasRoster: memberCount > 0,
            FactionCount: factions.Length,
            MemberCount: memberCount,
            CoverageRatio: state.CoverageRatio,
            OpenTargetCount: state.OpenTargetCount,
            HoleCount: state.Holes.Count,
            Heartbeat: ToHeartbeatDto(state),
            Warnings: state.Warnings,
            Errors: state.Errors,
            Factions: factions,
            Holes: state.Holes.Select(ToHoleDto).ToArray());
    }

    public static WarHealthDto ToHealthDto(this WarDerivedState state)
    {
        var status = ComputeStatus(state);
        var memberCount = state.Factions.Sum(f => f.Members.Count);

        return new WarHealthDto(
            Status: status,
            IsReady: status == "ok",
            WarId: state.WarId,
            AsOfUtc: state.AsOfUtc,
            HasRoster: memberCount > 0,
            FactionCount: state.Factions.Count,
            MemberCount: memberCount,
            CoverageRatio: state.CoverageRatio,
            OpenTargetCount: state.OpenTargetCount,
            HoleCount: state.Holes.Count,
            Heartbeat: ToHeartbeatDto(state),
            Warnings: state.Warnings,
            Errors: state.Errors);
    }

    private static string ComputeStatus(WarDerivedState state)
    {
        if (state.WarId is null)
            return "not-ready";

        if (state.Errors.Count > 0 || state.IsHeartbeatStale)
            return "degraded";

        return "ok";
    }

    private static WarHeartbeatDto ToHeartbeatDto(WarDerivedState state)
        => new(
            Phase: state.HeartbeatPhase,
            UpdatedAtUtc: state.HeartbeatUpdatedAtUtc,
            PollStartedAtUtc: state.HeartbeatPollStartedAtUtc,
            PollCompletedAtUtc: state.HeartbeatPollCompletedAtUtc,
            StaleAfterUtc: state.HeartbeatStaleAfterUtc,
            IsStale: state.IsHeartbeatStale,
            LastError: state.HeartbeatLastError);

    private static WarFactionDto ToFactionDto(WarDerivedFactionState faction)
        => new(
            FactionId: faction.FactionId,
            FactionName: faction.FactionName,
            Score: faction.Score,
            Chain: faction.Chain,
            RemainingScoreToWin: faction.RemainingScoreToWin,
            AvailableMemberCount: faction.AvailableMemberCount,
            HospitalizedMemberCount: faction.HospitalizedMemberCount,
            UnavailableMemberCount: faction.UnavailableMemberCount,
            CoverageRatio: faction.CoverageRatio,
            OpenTargetCount: faction.OpenTargetCount,
            TargetCoverageRatio: faction.TargetCoverageRatio,
            ChainCommand: ToChainCommandDto(faction),
            ScoreRate: new WarScoreRateDto(
                faction.ScoreRate.SampleCount,
                faction.ScoreRate.StartedAtUtc,
                faction.ScoreRate.EndedAtUtc,
                faction.ScoreRate.WindowSeconds,
                faction.ScoreRate.ScoreDelta,
                faction.ScoreRate.PointsPerMinute,
                faction.ScoreRate.IsAvailable,
                faction.ScoreRate.Diagnostic),
            Eta: new WarEtaDto(
                faction.Eta.RemainingScore,
                faction.Eta.SecondsUntilWin,
                faction.Eta.IsAvailable,
                faction.Eta.Diagnostic),
            AttacksToFinish: new WarAttacksToFinishDto(
                faction.AttacksToFinish.AverageScorePerAttack,
                faction.AttacksToFinish.RequiredAttacks,
                faction.AttacksToFinish.IsAvailable,
                faction.AttacksToFinish.Diagnostic),
            Members: faction.Members.Select(member => new WarMemberDto(
                member.MemberId,
                member.MemberName,
                member.Score,
                member.Chain,
                member.Attacks,
                member.StatusState,
                member.StatusUntilUtc,
                member.Availability.ToString().ToLowerInvariant(),
                member.HospitalCountdownSeconds,
                member.IsIdleAttacker,
                member.CapturedAtUtc)).ToArray());

    private static WarChainCommandDto? ToChainCommandDto(WarDerivedFactionState faction)
    {
        if (faction.ChainState is not { } chain)
        {
            return null;
        }

        var timer = faction.ChainTimer;
        return new WarChainCommandDto(
            ChainLength: chain.ChainLength,
            CurrentMultiplier: chain.CurrentMultiplier,
            NextMilestone: chain.NextMilestone,
            HitsToNextMilestone: chain.HitsToNextMilestone,
            NextMilestoneBonus: chain.NextMilestoneBonus,
            IsInReservationWindow: chain.IsInReservationWindow,
            ForfeitedValueIfCrossedOutside: chain.ForfeitedValueIfCrossedOutside,
            AttackableWarTargetCount: chain.AttackableWarTargetCount,
            Mode: chain.Mode.ToString(),
            Advice: chain.Reason,
            Alert: faction.ChainAlert.ToString(),
            TimerIsInferred: timer?.IsInferred ?? false,
            SecondsSinceLastHit: timer?.SecondsSinceLastIncrease,
            SecondsUntilLapse: timer?.SecondsUntilLapse,
            TimerSpacingSeconds: timer?.SampleSpacingSeconds ?? 0,
            TimerDiagnostic: timer?.Diagnostic,
            TimerConfidence: (timer?.Confidence ?? ChainLapseConfidence.None).ToString(),
            LapsesAtUtc: timer?.LapsesAtUtc);
    }

    private static WarHoleDto ToHoleDto(WarHoleRecord hole)
        => new(
            Kind: hole.Kind.ToString().ToLowerInvariant(),
            Severity: hole.Severity.ToString().ToLowerInvariant(),
            FactionId: hole.FactionId,
            FactionName: hole.FactionName,
            OpponentFactionId: hole.OpponentFactionId,
            MemberId: hole.MemberId,
            MemberName: hole.MemberName,
            Reason: hole.Reason);
}
