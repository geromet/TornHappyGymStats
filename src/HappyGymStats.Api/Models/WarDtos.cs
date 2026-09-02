using HappyGymStats.Core.War;

namespace HappyGymStats.Api.Models;

public sealed record WarStateDto(
    string Status,
    bool IsReady,
    long? WarId,
    DateTimeOffset AsOfUtc,
    bool HasRoster,
    int FactionCount,
    int MemberCount,
    decimal CoverageRatio,
    int OpenTargetCount,
    int HoleCount,
    WarHeartbeatDto Heartbeat,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyList<WarFactionDto> Factions,
    IReadOnlyList<WarHoleDto> Holes);

public sealed record WarHealthDto(
    string Status,
    bool IsReady,
    long? WarId,
    DateTimeOffset AsOfUtc,
    bool HasRoster,
    int FactionCount,
    int MemberCount,
    decimal CoverageRatio,
    int OpenTargetCount,
    int HoleCount,
    WarHeartbeatDto Heartbeat,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public sealed record WarHeartbeatDto(
    string? Phase,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? PollStartedAtUtc,
    DateTimeOffset? PollCompletedAtUtc,
    DateTimeOffset? StaleAfterUtc,
    bool IsStale,
    string? LastError);

public sealed record WarFactionDto(
    long FactionId,
    string FactionName,
    int Score,
    int Chain,
    int RemainingScoreToWin,
    int AvailableMemberCount,
    int HospitalizedMemberCount,
    int UnavailableMemberCount,
    decimal CoverageRatio,
    int OpenTargetCount,
    decimal TargetCoverageRatio,
    WarScoreRateDto ScoreRate,
    WarEtaDto Eta,
    WarAttacksToFinishDto AttacksToFinish,
    IReadOnlyList<WarMemberDto> Members);

public sealed record WarMemberDto(
    long MemberId,
    string MemberName,
    int Score,
    int Chain,
    int Attacks,
    string? StatusState,
    DateTimeOffset? StatusUntilUtc,
    string Availability,
    int HospitalCountdownSeconds,
    bool IsIdleAttacker,
    DateTimeOffset CapturedAtUtc);

public sealed record WarScoreRateDto(
    int SampleCount,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    int WindowSeconds,
    int ScoreDelta,
    decimal? PointsPerMinute,
    bool IsAvailable,
    string? Diagnostic);

public sealed record WarEtaDto(
    int RemainingScore,
    int? SecondsUntilWin,
    bool IsAvailable,
    string? Diagnostic);

public sealed record WarAttacksToFinishDto(
    decimal? AverageScorePerAttack,
    int? RequiredAttacks,
    bool IsAvailable,
    string? Diagnostic);

public sealed record WarHoleDto(
    string Kind,
    string Severity,
    long FactionId,
    string FactionName,
    long? OpponentFactionId,
    long MemberId,
    string MemberName,
    string Reason);

public sealed record WarNotifyAcceptedDto(string Status, WarStateDto State);

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
