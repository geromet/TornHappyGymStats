using HappyGymStats.Core.War;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class ChainLapseInferenceTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeSeconds(1_731_000_000);

    private static (DateTimeOffset, int)[] Series(int spacingSeconds, params int[] chains)
        => chains.Select((chain, i) => (T0.AddSeconds(i * spacingSeconds), chain)).ToArray();

    [Fact]
    public void Fewer_than_two_samples_yields_no_estimate()
    {
        var estimate = ChainLapseInference.Infer(Series(30, 12), T0.AddSeconds(60));

        Assert.Equal(ChainLapseConfidence.None, estimate.Confidence);
        Assert.Null(estimate.SecondsUntilLapse);
        Assert.False(estimate.IsInferred);
    }

    [Fact]
    public void A_chain_that_never_rises_in_the_window_reports_unknown_not_a_full_timer()
    {
        // Chain sat at 40 across a 2-minute window - the last hit is older than the data we hold.
        var estimate = ChainLapseInference.Infer(Series(30, 40, 40, 40, 40, 40), T0.AddSeconds(150));

        Assert.Equal(ChainLapseConfidence.None, estimate.Confidence);
        Assert.Null(estimate.SecondsSinceLastIncrease);
        Assert.Null(estimate.SecondsUntilLapse);
        Assert.Contains("older than", estimate.Diagnostic);
    }

    [Fact]
    public void A_chain_that_rose_then_lapsed_reports_unknown_not_a_walking_countdown()
    {
        // Rose to 41, then reset to 0 and stayed there. The last *increase* is real but belongs
        // to a chain that is already gone - do not emit a live timer that would eventually cross
        // the alert threshold for a dead chain.
        var estimate = ChainLapseInference.Infer(Series(30, 40, 41, 0, 0), T0.AddSeconds(120));

        Assert.Equal(ChainLapseConfidence.None, estimate.Confidence);
        Assert.Null(estimate.SecondsUntilLapse);
        Assert.Contains("lapsed", estimate.Diagnostic);
    }

    [Fact]
    public void A_chain_still_climbing_after_a_reset_is_dated_to_the_new_chains_last_hit()
    {
        // 40 -> reset 0 -> 1 -> 2. The live chain is the new one; its last hit is at t=90.
        var estimate = ChainLapseInference.Infer(Series(30, 40, 0, 1, 2), T0.AddSeconds(120));

        Assert.Equal(ChainLapseConfidence.Inferred, estimate.Confidence);
        Assert.Equal(T0.AddSeconds(90), estimate.LastChainIncreaseAtUtc);
        Assert.Equal(30, estimate.SecondsSinceLastIncrease);
    }

    [Fact]
    public void No_live_chain_reports_nothing_to_show()
    {
        var estimate = ChainLapseInference.Infer(Series(30, 0, 0, 0), T0.AddSeconds(90));

        Assert.Equal(ChainLapseConfidence.None, estimate.Confidence);
        Assert.Contains("No live chain", estimate.Diagnostic);
    }

    [Fact]
    public void A_rising_chain_dates_the_last_hit_to_the_last_observed_increase()
    {
        // Increases at index 1 (t=30) and index 3 (t=90); last is t=90. "Now" is t=150 => 60s since.
        var estimate = ChainLapseInference.Infer(Series(30, 10, 11, 11, 12, 12), T0.AddSeconds(150));

        Assert.Equal(ChainLapseConfidence.Inferred, estimate.Confidence);
        Assert.True(estimate.IsInferred);
        Assert.Equal(T0.AddSeconds(90), estimate.LastChainIncreaseAtUtc);
        Assert.Equal(60, estimate.SecondsSinceLastIncrease);
        Assert.Equal(ChainLapseInference.TornChainLapseTimeoutSeconds - 60, estimate.SecondsUntilLapse);
        Assert.Equal(30, estimate.SampleSpacingSeconds);
    }

    [Fact]
    public void Seconds_until_lapse_floors_at_zero_when_the_timer_is_already_blown()
    {
        var estimate = ChainLapseInference.Infer(Series(30, 10, 11), T0.AddSeconds(30 + 600));

        Assert.Equal(0, estimate.SecondsUntilLapse);
        Assert.Equal(600, estimate.SecondsSinceLastIncrease);
    }

    [Fact]
    public void Sample_spacing_is_the_median_gap_and_survives_one_long_stall()
    {
        // Gaps: 30, 30, 300, 30 -> median 30. The stall must not inflate the error bar.
        var samples = new[]
        {
            (T0, 10),
            (T0.AddSeconds(30), 11),
            (T0.AddSeconds(60), 12),
            (T0.AddSeconds(360), 13),
            (T0.AddSeconds(390), 14),
        };

        var estimate = ChainLapseInference.Infer(samples, T0.AddSeconds(420));

        Assert.Equal(30, estimate.SampleSpacingSeconds);
    }

    [Fact]
    public void Unordered_input_is_sorted_before_inference()
    {
        var samples = new[]
        {
            (T0.AddSeconds(90), 12),
            (T0, 10),
            (T0.AddSeconds(30), 11),
        };

        var estimate = ChainLapseInference.Infer(samples, T0.AddSeconds(120));

        Assert.Equal(T0.AddSeconds(90), estimate.LastChainIncreaseAtUtc);
        Assert.Equal(30, estimate.SecondsSinceLastIncrease);
    }

    [Fact]
    public void ChainLapseInference_timeout_constant_is_challengeable()
    {
        // data/V2/handoff/00-brief.md: an unverified assumption gets a named constant, a comment
        // pointing at the ledger, and a test that fails loudly if reality (or a careless edit)
        // disagrees. 300s is the Torn community chain-lapse timeout. If S01's live sweep gives a
        // real `timeout`, update BOTH this value and the doc, then delete the inference path.
        Assert.Equal(300, ChainLapseInference.TornChainLapseTimeoutSeconds);
    }
}
