namespace HappyGymStats.Core.War;

public enum WarReadinessState
{
    Ready = 1,
    Limited = 2,
    Unavailable = 3,
}

public enum WarReadinessWindowStatus
{
    MissingResponse = 0,
    BeforeDeclaredWindow = 1,
    InDeclaredWindow = 2,
    AfterDeclaredWindow = 3,
}

/// <summary>
/// Member-authored readiness only. This is deliberately not an observed Torn state and must not
/// be interpreted as one. UTC windows describe when the member says this declaration applies.
/// </summary>
public sealed record WarReadinessDeclaration
{
    public required long FactionId { get; init; }
    public required long WarId { get; init; }
    public required long MemberId { get; init; }
    public required WarReadinessState State { get; init; }
    public required DateTimeOffset WindowStartUtc { get; init; }
    public required DateTimeOffset WindowEndUtc { get; init; }
    public string? Note { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public required long Revision { get; init; }

    public static WarReadinessDeclaration Create(
        long factionId,
        long warId,
        long memberId,
        WarReadinessState state,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        string? note,
        DateTimeOffset updatedAtUtc,
        long revision)
    {
        if (factionId <= 0) throw new ArgumentOutOfRangeException(nameof(factionId));
        if (warId <= 0) throw new ArgumentOutOfRangeException(nameof(warId));
        if (memberId <= 0) throw new ArgumentOutOfRangeException(nameof(memberId));
        if (!Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state));
        if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));

        RequireUtc(windowStartUtc, nameof(windowStartUtc));
        RequireUtc(windowEndUtc, nameof(windowEndUtc));
        RequireUtc(updatedAtUtc, nameof(updatedAtUtc));

        if (windowEndUtc <= windowStartUtc)
            throw new ArgumentException("Readiness window must end after it starts.", nameof(windowEndUtc));

        if (updatedAtUtc > windowEndUtc)
            throw new ArgumentException("Declaration update time cannot be after its applicability window ends.", nameof(updatedAtUtc));

        var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (normalizedNote?.Length > 500)
            throw new ArgumentOutOfRangeException(nameof(note), "Readiness note cannot exceed 500 characters.");

        return new WarReadinessDeclaration
        {
            FactionId = factionId,
            WarId = warId,
            MemberId = memberId,
            State = state,
            WindowStartUtc = windowStartUtc,
            WindowEndUtc = windowEndUtc,
            Note = normalizedNote,
            UpdatedAtUtc = updatedAtUtc,
            Revision = revision,
        };
    }

    private static void RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Readiness timestamps must be explicit UTC values.", parameterName);
    }
}

public sealed record SetWarReadinessCommand(
    long ActorMemberId,
    long TargetMemberId,
    long FactionId,
    long WarId,
    WarReadinessState State,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    string? Note,
    DateTimeOffset UpdatedAtUtc);

public sealed record ClearWarReadinessCommand(
    long ActorMemberId,
    long TargetMemberId,
    long FactionId,
    long WarId);

/// <summary>
/// Owns mutation invariants before persistence. A planner may read declarations, but only the
/// target member may set or clear their declaration through this policy.
/// </summary>
public static class WarReadinessMutationPolicy
{
    public static WarReadinessDeclaration Set(
        WarReadinessDeclaration? existing,
        SetWarReadinessCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureSelf(command.ActorMemberId, command.TargetMemberId);
        EnsureScope(existing, command.TargetMemberId, command.FactionId, command.WarId);

        var nextRevision = existing is null ? 1 : checked(existing.Revision + 1);
        return WarReadinessDeclaration.Create(
            command.FactionId,
            command.WarId,
            command.TargetMemberId,
            command.State,
            command.WindowStartUtc,
            command.WindowEndUtc,
            command.Note,
            command.UpdatedAtUtc,
            nextRevision);
    }

    public static bool CanClear(WarReadinessDeclaration? existing, ClearWarReadinessCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureSelf(command.ActorMemberId, command.TargetMemberId);
        EnsureScope(existing, command.TargetMemberId, command.FactionId, command.WarId);
        return existing is not null;
    }

    private static void EnsureSelf(long actorMemberId, long targetMemberId)
    {
        if (actorMemberId <= 0) throw new ArgumentOutOfRangeException(nameof(actorMemberId));
        if (targetMemberId <= 0) throw new ArgumentOutOfRangeException(nameof(targetMemberId));
        if (actorMemberId != targetMemberId)
            throw new UnauthorizedAccessException("A member may only mutate their own readiness declaration.");
    }

    private static void EnsureScope(
        WarReadinessDeclaration? existing,
        long memberId,
        long factionId,
        long warId)
    {
        if (factionId <= 0) throw new ArgumentOutOfRangeException(nameof(factionId));
        if (warId <= 0) throw new ArgumentOutOfRangeException(nameof(warId));

        if (existing is not null &&
            (existing.MemberId != memberId || existing.FactionId != factionId || existing.WarId != warId))
        {
            throw new InvalidOperationException("Existing readiness declaration belongs to a different member or war scope.");
        }
    }
}

public sealed record WarReadinessMemberProjection(
    long MemberId,
    WarReadinessState? DeclaredState,
    WarReadinessWindowStatus WindowStatus,
    DateTimeOffset? WindowStartUtc,
    DateTimeOffset? WindowEndUtc,
    string? Note,
    DateTimeOffset? UpdatedAtUtc,
    long? Revision);

public sealed record WarReadinessPlannerSnapshot(
    long FactionId,
    long WarId,
    DateTimeOffset AsOfUtc,
    int RosterMemberCount,
    int RespondedMemberCount,
    int MissingResponseCount,
    decimal ResponseCoverage,
    IReadOnlyList<WarReadinessMemberProjection> Members);

/// <summary>
/// Builds planner coverage from member-authored declarations only. Missing responses remain
/// missing and declarations outside their window remain outside-window; neither is converted to
/// observed activity, inactivity, or misconduct.
/// </summary>
public static class WarReadinessPlanner
{
    public static WarReadinessPlannerSnapshot Build(
        long factionId,
        long warId,
        IReadOnlyCollection<long> rosterMemberIds,
        IReadOnlyCollection<WarReadinessDeclaration> declarations,
        DateTimeOffset asOfUtc)
    {
        if (factionId <= 0) throw new ArgumentOutOfRangeException(nameof(factionId));
        if (warId <= 0) throw new ArgumentOutOfRangeException(nameof(warId));
        ArgumentNullException.ThrowIfNull(rosterMemberIds);
        ArgumentNullException.ThrowIfNull(declarations);
        if (asOfUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Planner evaluation time must be explicit UTC.", nameof(asOfUtc));

        var roster = rosterMemberIds.ToArray();
        if (roster.Any(id => id <= 0))
            throw new ArgumentOutOfRangeException(nameof(rosterMemberIds), "Roster member ids must be positive.");
        if (roster.Distinct().Count() != roster.Length)
            throw new ArgumentException("Roster member ids must be unique.", nameof(rosterMemberIds));

        var byMember = new Dictionary<long, WarReadinessDeclaration>();
        foreach (var declaration in declarations)
        {
            if (declaration.FactionId != factionId || declaration.WarId != warId)
                throw new InvalidOperationException("Readiness declaration belongs to a different faction or war.");
            if (!roster.Contains(declaration.MemberId))
                throw new InvalidOperationException("Readiness declaration belongs to a member outside the supplied roster.");
            if (!byMember.TryAdd(declaration.MemberId, declaration))
                throw new InvalidOperationException("Multiple readiness declarations were supplied for the same member.");
        }

        var members = roster
            .OrderBy(id => id)
            .Select(memberId => Project(memberId, byMember.GetValueOrDefault(memberId), asOfUtc))
            .ToArray();

        var responded = members.Count(member => member.DeclaredState is not null);
        var rosterCount = members.Length;
        return new WarReadinessPlannerSnapshot(
            factionId,
            warId,
            asOfUtc,
            rosterCount,
            responded,
            rosterCount - responded,
            rosterCount == 0 ? 0m : (decimal)responded / rosterCount,
            members);
    }

    private static WarReadinessMemberProjection Project(
        long memberId,
        WarReadinessDeclaration? declaration,
        DateTimeOffset asOfUtc)
    {
        if (declaration is null)
        {
            return new WarReadinessMemberProjection(
                memberId,
                null,
                WarReadinessWindowStatus.MissingResponse,
                null,
                null,
                null,
                null,
                null);
        }

        var windowStatus = asOfUtc < declaration.WindowStartUtc
            ? WarReadinessWindowStatus.BeforeDeclaredWindow
            : asOfUtc >= declaration.WindowEndUtc
                ? WarReadinessWindowStatus.AfterDeclaredWindow
                : WarReadinessWindowStatus.InDeclaredWindow;

        return new WarReadinessMemberProjection(
            memberId,
            declaration.State,
            windowStatus,
            declaration.WindowStartUtc,
            declaration.WindowEndUtc,
            declaration.Note,
            declaration.UpdatedAtUtc,
            declaration.Revision);
    }
}
