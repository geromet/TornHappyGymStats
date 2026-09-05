using HappyGymStats.Core.War;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarReplayValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 11, 0, 0, TimeSpan.Zero);

    // The public record constructor is intentionally exercised directly: evaluator entry points
    // must fail closed even when callers bypass WarReplayObservation.Create's convenience checks.
    [Theory]
    [InlineData(0L, 1, 1, 1, 1, 100)]
    [InlineData(77L, -1, 1, 1, 1, 100)]
    [InlineData(77L, 1, -1, 1, 1, 100)]
    [InlineData(77L, 1, 1, -1, 1, 100)]
    [InlineData(77L, 1, 1, 1, -1, 100)]
    [InlineData(77L, 1, 1, 1, 1, 0)]
    public void Timeline_revalidates_public_record_constructor_inputs(
        long warId,
        int factionScore,
        int opponentScore,
        int factionChain,
        int opponentChain,
        int targetScore)
    {
        var malformed = new WarReplayObservation(
            warId,
            Now,
            Now,
            factionScore,
            opponentScore,
            factionChain,
            opponentChain,
            targetScore);

        Assert.ThrowsAny<ArgumentException>(() => WarReplay.ValidateTimeline([malformed]));
        Assert.ThrowsAny<ArgumentException>(() => WarReplay.PredictLinearBaseline([malformed], Now));
    }
}
