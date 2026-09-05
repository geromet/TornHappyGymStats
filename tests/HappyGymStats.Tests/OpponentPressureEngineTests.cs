using System.Reflection;
using HappyGymStats.Core.War;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class OpponentPressureEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Sparse_coverage_is_unknown_instead_of_normal()
    {
        var result = OpponentPressureEngine.Evaluate(Input(observed: 4, active: 1, attackable: 1));

        Assert.Equal(OpponentPressureLevel.Unknown, result.Level);
        Assert.Contains("only 4 members observed", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Stale_observation_is_unknown_and_preserves_auditable_evidence()
    {
        var input = Input(active: 8, attackable: 7) with
        {
            FreshestObservationAtUtc = Now.AddMinutes(-16),
            WindowStartUtc = Now.AddMinutes(-20),
            Provenance = ["faction-members:sample-42"],
        };

        var result = OpponentPressureEngine.Evaluate(input);

        Assert.Equal(OpponentPressureLevel.Unknown, result.Level);
        Assert.Equal(8, result.ActiveMemberCount);
        Assert.Equal(0.75m, result.Coverage);
        Assert.Equal("faction-members:sample-42", Assert.Single(result.Provenance));
        Assert.Contains("16 minutes old", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void One_member_noise_does_not_create_surge()
    {
        var result = OpponentPressureEngine.Evaluate(Input(
            observed: 12,
            active: 3,
            attackable: 2,
            baselineActive: 0.10m,
            baselineAttackable: 0.08m,
            transitions: 1,
            attacks: 1));

        Assert.NotEqual(OpponentPressureLevel.Surge, result.Level);
    }

    [Fact]
    public void Coordinated_relative_increase_produces_surge_with_explanation()
    {
        var result = OpponentPressureEngine.Evaluate(Input(
            observed: 20,
            active: 11,
            attackable: 10,
            baselineActive: 0.20m,
            baselineAttackable: 0.15m,
            transitions: 5));

        Assert.Equal(OpponentPressureLevel.Surge, result.Level);
        Assert.Contains("11/20 active", result.Explanation, StringComparison.Ordinal);
        Assert.Contains("5 synchronized transitions", result.Explanation, StringComparison.Ordinal);
        Assert.Equal(40, result.BaselineSampleCount);
    }

    [Fact]
    public void Relative_increase_without_surge_coordination_is_elevated()
    {
        var result = OpponentPressureEngine.Evaluate(Input(
            observed: 20,
            active: 8,
            attackable: 6,
            baselineActive: 0.20m,
            baselineAttackable: 0.15m));

        Assert.Equal(OpponentPressureLevel.Elevated, result.Level);
    }

    [Fact]
    public void Surge_downgrade_is_held_during_cooldown()
    {
        var input = Input(observed: 20, active: 4, attackable: 3) with
        {
            PriorState = new OpponentPressurePriorState
            {
                Level = OpponentPressureLevel.Surge,
                SinceUtc = Now.AddMinutes(-4),
            },
        };

        var result = OpponentPressureEngine.Evaluate(input);

        Assert.Equal(OpponentPressureLevel.Surge, result.Level);
        Assert.True(result.HeldByHysteresis);
        Assert.Contains("held at Surge", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Falling_below_threshold_clears_deterministically_after_cooldown()
    {
        var input = Input(observed: 20, active: 4, attackable: 3) with
        {
            PriorState = new OpponentPressurePriorState
            {
                Level = OpponentPressureLevel.Surge,
                SinceUtc = Now.AddMinutes(-11),
            },
        };

        var result = OpponentPressureEngine.Evaluate(input);

        Assert.Equal(OpponentPressureLevel.Normal, result.Level);
        Assert.False(result.HeldByHysteresis);
    }

    [Fact]
    public void Loss_of_fresh_evidence_overrides_hysteresis_to_unknown()
    {
        var input = Input(observed: 20, active: 10, attackable: 9) with
        {
            FreshestObservationAtUtc = Now.AddHours(-1),
            WindowStartUtc = Now.AddHours(-2),
            PriorState = new OpponentPressurePriorState
            {
                Level = OpponentPressureLevel.Surge,
                SinceUtc = Now.AddMinutes(-1),
            },
        };

        var result = OpponentPressureEngine.Evaluate(input);

        Assert.Equal(OpponentPressureLevel.Unknown, result.Level);
        Assert.False(result.HeldByHysteresis);
    }

    [Fact]
    public void Public_engine_surface_accepts_only_preassembled_observation_data()
    {
        var method = Assert.Single(typeof(OpponentPressureEngine).GetMethods(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        var parameter = Assert.Single(method.GetParameters());

        Assert.Equal(nameof(OpponentPressureEngine.Evaluate), method.Name);
        Assert.Equal(typeof(OpponentPressureInput), parameter.ParameterType);
        Assert.Equal(typeof(OpponentPressureSignal), method.ReturnType);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(10, 11, 0)]
    [InlineData(10, 5, 11)]
    public void Invalid_member_counts_fail_closed(int observed, int active, int attackable)
    {
        Assert.ThrowsAny<ArgumentException>(() => OpponentPressureEngine.Evaluate(
            Input(observed: observed, active: active, attackable: attackable)));
    }

    [Fact]
    public void Observation_before_declared_window_fails_closed()
    {
        var input = Input() with
        {
            WindowStartUtc = Now.AddMinutes(-5),
            FreshestObservationAtUtc = Now.AddMinutes(-6),
        };

        Assert.Throws<ArgumentException>(() => OpponentPressureEngine.Evaluate(input));
    }

    [Fact]
    public void Undefined_prior_pressure_level_fails_closed()
    {
        var input = Input() with
        {
            PriorState = new OpponentPressurePriorState
            {
                Level = (OpponentPressureLevel)999,
                SinceUtc = Now.AddMinutes(-1),
            },
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => OpponentPressureEngine.Evaluate(input));
    }

    private static OpponentPressureInput Input(
        int observed = 15,
        int active = 4,
        int attackable = 3,
        decimal? baselineActive = 0.25m,
        decimal? baselineAttackable = 0.20m,
        int transitions = 0,
        int attacks = 0)
    {
        return new OpponentPressureInput
        {
            AsOfUtc = Now,
            WindowStartUtc = Now.AddMinutes(-5),
            FreshestObservationAtUtc = Now.AddMinutes(-1),
            FactionMemberCount = 20,
            ObservedMemberCount = observed,
            ActiveMemberCount = active,
            AttackableMemberCount = attackable,
            SynchronizedAttackableTransitions = transitions,
            RecentAttackCount = attacks,
            BaselineActiveShare = baselineActive,
            BaselineAttackableShare = baselineAttackable,
            BaselineSampleCount = 40,
            Provenance = ["faction-members:sample-41", "war-attacks:existing-stream"],
        };
    }
}
