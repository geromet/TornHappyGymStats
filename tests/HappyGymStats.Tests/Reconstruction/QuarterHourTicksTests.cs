using HappyGymStats.Reconstruction;

namespace HappyGymStats.Tests.Reconstruction;

public sealed class QuarterHourTicksTests
{
    [Fact]
    public void CountTicksBetweenUtc_WhenSameInstantOnTickBoundary_ReturnsZero()
    {
        var t = new DateTimeOffset(2026, 01, 01, 12, 15, 00, TimeSpan.Zero);
        var ticks = QuarterHourTicks.CountTicksBetweenUtc(t, t);
        Assert.Equal(0, ticks);
    }

    [Fact]
    public void CountTicksBetweenUtc_WhenCrossingIntoTickBoundary_CountsLaterInclusive()
    {
        var earlier = new DateTimeOffset(2026, 01, 01, 12, 14, 59, TimeSpan.Zero);
        var later = new DateTimeOffset(2026, 01, 01, 12, 15, 00, TimeSpan.Zero);

        var ticks = QuarterHourTicks.CountTicksBetweenUtc(earlier, later);

        Assert.Equal(1, ticks);
    }

    [Fact]
    public void CountTicksBetweenUtc_WhenEarlierIsExactlyOnTickBoundary_IsEarlierExclusive()
    {
        var earlier = new DateTimeOffset(2026, 01, 01, 12, 15, 00, TimeSpan.Zero);
        var later = new DateTimeOffset(2026, 01, 01, 12, 15, 01, TimeSpan.Zero);

        var ticks = QuarterHourTicks.CountTicksBetweenUtc(earlier, later);

        Assert.Equal(0, ticks);
    }

    [Fact]
    public void CountTicksBetweenUtc_WhenMultipleTicksInRange_CountsAllBoundaries()
    {
        var earlier = new DateTimeOffset(2026, 01, 01, 12, 00, 00, TimeSpan.Zero);
        var later = new DateTimeOffset(2026, 01, 01, 13, 00, 00, TimeSpan.Zero);

        // Strictly after 12:00 and <= 13:00 => 12:15, 12:30, 12:45, 13:00
        var ticks = QuarterHourTicks.CountTicksBetweenUtc(earlier, later);

        Assert.Equal(4, ticks);
    }

    [Fact]
    public void CountTicksBetweenUtc_WhenCrossingMidnight_CountsCrossDayTickBoundaries()
    {
        var earlier = new DateTimeOffset(2026, 01, 01, 23, 45, 00, TimeSpan.Zero);
        var later = new DateTimeOffset(2026, 01, 02, 00, 15, 00, TimeSpan.Zero);

        // Strictly after 23:45 and <= 00:15 => 00:00, 00:15
        var ticks = QuarterHourTicks.CountTicksBetweenUtc(earlier, later);

        Assert.Equal(2, ticks);
    }

    [Fact]
    public void EnumerateTickInstantsBetweenUtc_MatchesCountTicksBetweenUtc()
    {
        var earlier = new DateTimeOffset(2026, 01, 01, 12, 00, 00, TimeSpan.Zero);
        var later = new DateTimeOffset(2026, 01, 01, 13, 00, 00, TimeSpan.Zero);

        var ticks = QuarterHourTicks.EnumerateTickInstantsBetweenUtc(earlier, later).ToList();

        Assert.Equal(4, ticks.Count);
        Assert.Equal(new DateTimeOffset(2026, 01, 01, 12, 15, 00, TimeSpan.Zero), ticks[0]);
        Assert.Equal(new DateTimeOffset(2026, 01, 01, 12, 30, 00, TimeSpan.Zero), ticks[1]);
        Assert.Equal(new DateTimeOffset(2026, 01, 01, 12, 45, 00, TimeSpan.Zero), ticks[2]);
        Assert.Equal(new DateTimeOffset(2026, 01, 01, 13, 00, 00, TimeSpan.Zero), ticks[3]);

        Assert.Equal(QuarterHourTicks.CountTicksBetweenUtc(earlier, later), ticks.Count);
    }

    [Fact]
    public void CountTicksBetweenUtc_WhenNotUtc_Throws()
    {
        var earlier = new DateTimeOffset(2026, 01, 01, 12, 00, 00, TimeSpan.FromHours(+1));
        var later = new DateTimeOffset(2026, 01, 01, 13, 00, 00, TimeSpan.Zero);

        var ex = Assert.Throws<ArgumentException>(() => QuarterHourTicks.CountTicksBetweenUtc(earlier, later));
        Assert.Equal("earlierUtc", ex.ParamName);
    }

    [Fact]
    public void CountTicksBetweenUtc_WhenLaterIsEarlierThanEarlier_Throws()
    {
        var earlier = new DateTimeOffset(2026, 01, 01, 13, 00, 00, TimeSpan.Zero);
        var later = new DateTimeOffset(2026, 01, 01, 12, 59, 59, TimeSpan.Zero);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => QuarterHourTicks.CountTicksBetweenUtc(earlier, later));
        Assert.Equal("laterUtc", ex.ParamName);
    }
}
