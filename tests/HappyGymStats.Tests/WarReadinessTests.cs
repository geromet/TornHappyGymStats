using HappyGymStats.Core.War;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarReadinessTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Member_can_set_and_update_only_their_own_declaration()
    {
        var first = WarReadinessMutationPolicy.Set(null, Command(memberId: 42, state: WarReadinessState.Ready));
        var second = WarReadinessMutationPolicy.Set(first, Command(memberId: 42, state: WarReadinessState.Limited));

        Assert.Equal(1, first.Revision);
        Assert.Equal(2, second.Revision);
        Assert.Equal(WarReadinessState.Limited, second.State);

        var чужой = Command(memberId: 42, state: WarReadinessState.Ready) with { ActorMemberId = 99 };
        Assert.Throws<UnauthorizedAccessException>(() => WarReadinessMutationPolicy.Set(second, чужой));
    }

    [Fact]
    public void Member_can_clear_only_their_own_matching_scope()
    {
        var declaration = WarReadinessMutationPolicy.Set(null, Command(memberId: 42));

        Assert.True(WarReadinessMutationPolicy.CanClear(
            declaration,
            new ClearWarReadinessCommand(42, 42, 100, 200)));

        Assert.Throws<UnauthorizedAccessException>(() => WarReadinessMutationPolicy.CanClear(
            declaration,
            new ClearWarReadinessCommand(99, 42, 100, 200)));

        Assert.Throws<InvalidOperationException>(() => WarReadinessMutationPolicy.CanClear(
            declaration,
            new ClearWarReadinessCommand(42, 42, 100, 201)));
    }

    [Fact]
    public void Explicit_utc_and_valid_window_are_required()
    {
        var offsetStart = new DateTimeOffset(2026, 9, 5, 14, 0, 0, TimeSpan.FromHours(2));
        var command = Command(memberId: 42) with { WindowStartUtc = offsetStart };
        Assert.Throws<ArgumentException>(() => WarReadinessMutationPolicy.Set(null, command));

        var inverted = Command(memberId: 42) with
        {
            WindowStartUtc = Now.AddHours(2),
            WindowEndUtc = Now.AddHours(1),
        };
        Assert.Throws<ArgumentException>(() => WarReadinessMutationPolicy.Set(null, inverted));
    }

    [Fact]
    public void Planner_preserves_missing_response_and_declared_state_separately()
    {
        var declarations = new[]
        {
            WarReadinessMutationPolicy.Set(null, Command(memberId: 10, state: WarReadinessState.Ready)),
            WarReadinessMutationPolicy.Set(null, Command(memberId: 20, state: WarReadinessState.Unavailable)),
        };

        var snapshot = WarReadinessPlanner.Build(100, 200, new long[] { 10, 20, 30, 40 }, declarations, Now);

        Assert.Equal(4, snapshot.RosterMemberCount);
        Assert.Equal(2, snapshot.RespondedMemberCount);
        Assert.Equal(2, snapshot.MissingResponseCount);
        Assert.Equal(0.5m, snapshot.ResponseCoverage);

        var ready = snapshot.Members.Single(member => member.MemberId == 10);
        Assert.Equal(WarReadinessState.Ready, ready.DeclaredState);
        Assert.Equal(WarReadinessWindowStatus.InDeclaredWindow, ready.WindowStatus);

        var unavailable = snapshot.Members.Single(member => member.MemberId == 20);
        Assert.Equal(WarReadinessState.Unavailable, unavailable.DeclaredState);

        var missing = snapshot.Members.Single(member => member.MemberId == 30);
        Assert.Null(missing.DeclaredState);
        Assert.Equal(WarReadinessWindowStatus.MissingResponse, missing.WindowStatus);
    }

    [Fact]
    public void Outside_window_is_not_rewritten_as_unavailable()
    {
        var future = WarReadinessMutationPolicy.Set(null, Command(memberId: 10, state: WarReadinessState.Ready) with
        {
            WindowStartUtc = Now.AddHours(1),
            WindowEndUtc = Now.AddHours(3),
        });

        var snapshot = WarReadinessPlanner.Build(100, 200, new long[] { 10 }, new[] { future }, Now);
        var member = Assert.Single(snapshot.Members);

        Assert.Equal(WarReadinessState.Ready, member.DeclaredState);
        Assert.Equal(WarReadinessWindowStatus.BeforeDeclaredWindow, member.WindowStatus);
    }

    [Fact]
    public void Window_end_is_exclusive_and_expired_declaration_remains_a_response()
    {
        var declaration = WarReadinessMutationPolicy.Set(null, Command(memberId: 10) with
        {
            WindowStartUtc = Now.AddHours(-2),
            WindowEndUtc = Now,
            UpdatedAtUtc = Now.AddHours(-1),
        });

        var snapshot = WarReadinessPlanner.Build(100, 200, new long[] { 10 }, new[] { declaration }, Now);
        var member = Assert.Single(snapshot.Members);

        Assert.Equal(1, snapshot.RespondedMemberCount);
        Assert.Equal(WarReadinessWindowStatus.AfterDeclaredWindow, member.WindowStatus);
        Assert.Equal(WarReadinessState.Ready, member.DeclaredState);
    }

    [Fact]
    public void Planner_rejects_duplicate_or_cross_scope_declarations()
    {
        var declaration = WarReadinessMutationPolicy.Set(null, Command(memberId: 10));

        Assert.Throws<InvalidOperationException>(() => WarReadinessPlanner.Build(
            100,
            200,
            new long[] { 10 },
            new[] { declaration, declaration },
            Now));

        var otherWar = WarReadinessMutationPolicy.Set(null, Command(memberId: 10) with { WarId = 201 });
        Assert.Throws<InvalidOperationException>(() => WarReadinessPlanner.Build(
            100,
            200,
            new long[] { 10 },
            new[] { otherWar },
            Now));
    }

    [Fact]
    public void Note_is_trimmed_bounded_and_does_not_change_authority()
    {
        var declaration = WarReadinessMutationPolicy.Set(null, Command(memberId: 42) with { Note = "  online after dinner  " });
        Assert.Equal("online after dinner", declaration.Note);

        var oversized = Command(memberId: 42) with { Note = new string('x', 501) };
        Assert.Throws<ArgumentOutOfRangeException>(() => WarReadinessMutationPolicy.Set(null, oversized));
    }

    [Fact]
    public void Empty_roster_has_zero_coverage_not_fake_full_coverage()
    {
        var snapshot = WarReadinessPlanner.Build(100, 200, Array.Empty<long>(), Array.Empty<WarReadinessDeclaration>(), Now);

        Assert.Equal(0, snapshot.RosterMemberCount);
        Assert.Equal(0, snapshot.RespondedMemberCount);
        Assert.Equal(0m, snapshot.ResponseCoverage);
        Assert.Empty(snapshot.Members);
    }

    private static SetWarReadinessCommand Command(
        long memberId,
        WarReadinessState state = WarReadinessState.Ready)
        => new(
            ActorMemberId: memberId,
            TargetMemberId: memberId,
            FactionId: 100,
            WarId: 200,
            State: state,
            WindowStartUtc: Now.AddHours(-1),
            WindowEndUtc: Now.AddHours(4),
            Note: null,
            UpdatedAtUtc: Now.AddMinutes(-5));
}
