using System.Text.Json;
using HappyGymStats.Core.War;
using HappyGymStats.Data.Entities;
using Xunit;
using Xunit.Sdk;

namespace HappyGymStats.Tests;

public sealed class WarStateDerivationEngineTests
{
    private static readonly DateTimeOffset FixtureCapturedAtUtc = DateTimeOffset.FromUnixTimeSeconds(1731001800);
    private static readonly DateTimeOffset FixtureNowUtc = DateTimeOffset.FromUnixTimeSeconds(1731001800);
    private static readonly DateTimeOffset PriorSampleUtc = DateTimeOffset.FromUnixTimeSeconds(1731000900);

    [Fact]
    public void Derive_fixture_backed_state_computes_availability_rates_eta_holes_and_coverage()
    {
        var report = DeserializeFixture<RankedWarReportResponse>("tests/fixtures/war/ranked-war-report-48377.json");
        var roster = MapRoster(report, FixtureCapturedAtUtc);
        var samples = BuildFixtureSamples(report.War.WarId);

        var state = new WarStateDerivationEngine().Derive(roster, samples, FixtureNowUtc, report.IdleAttackers);

        Assert.Equal(48377, state.WarId);
        Assert.Equal(FixtureCapturedAtUtc, state.RosterCapturedAtUtc);
        Assert.Equal(PriorSampleUtc, state.ScoreWindowStartedAtUtc);
        Assert.Equal(FixtureCapturedAtUtc, state.ScoreWindowEndedAtUtc);
        Assert.Equal(2, state.ScoreSampleCount);
        Assert.Equal(0.5m, state.CoverageRatio);
        Assert.Empty(state.Errors);

        var home = Assert.Single(state.Factions.Where(faction => faction.FactionId == 111));
        Assert.Equal(128, home.Score);
        Assert.Equal(42, home.Chain);
        Assert.Equal(2, home.AvailableMemberCount);
        Assert.Equal(1, home.HospitalizedMemberCount);
        Assert.Equal(0, home.UnavailableMemberCount);
        Assert.Equal(872, home.RemainingScoreToWin);
        Assert.Equal(0.5m, home.CoverageRatio);
        Assert.True(home.ScoreRate.IsAvailable);
        Assert.Equal(28, home.ScoreRate.ScoreDelta);
        Assert.Equal(900, home.ScoreRate.WindowSeconds);
        Assert.Equal(1.8667m, home.ScoreRate.PointsPerMinute);
        Assert.True(home.Eta.IsAvailable);
        Assert.Equal(28029, home.Eta.SecondsUntilWin);
        Assert.True(home.AttacksToFinish.IsAvailable);
        Assert.Equal(10.6667m, home.AttacksToFinish.AverageScorePerAttack);
        Assert.Equal(82, home.AttacksToFinish.RequiredAttacks);

        var alice = Assert.Single(home.Members.Where(member => member.MemberId == 1001));
        Assert.Equal(WarMemberAvailabilityKind.Hospitalized, alice.Availability);
        Assert.Equal(1800, alice.HospitalCountdownSeconds);

        var bob = Assert.Single(home.Members.Where(member => member.MemberId == 1002));
        Assert.Equal(WarMemberAvailabilityKind.Available, bob.Availability);
        Assert.False(bob.IsIdleAttacker);

        var cara = Assert.Single(home.Members.Where(member => member.MemberId == 1003));
        Assert.Equal(WarMemberAvailabilityKind.Available, cara.Availability);
        Assert.True(cara.IsIdleAttacker);
        Assert.Equal(0, cara.HospitalCountdownSeconds);

        var away = Assert.Single(state.Factions.Where(faction => faction.FactionId == 222));
        Assert.Equal(117, away.Score);
        Assert.Equal(39, away.Chain);
        Assert.Equal(0, away.AvailableMemberCount);
        Assert.Equal(1, away.HospitalizedMemberCount);
        Assert.Equal(1, away.UnavailableMemberCount);
        Assert.Equal(883, away.RemainingScoreToWin);
        Assert.Equal(1m, away.CoverageRatio);
        Assert.True(away.ScoreRate.IsAvailable);
        Assert.Equal(27, away.ScoreRate.ScoreDelta);
        Assert.Equal(1.8m, away.ScoreRate.PointsPerMinute);
        Assert.True(away.Eta.IsAvailable);
        Assert.Equal(29434, away.Eta.SecondsUntilWin);
        Assert.True(away.AttacksToFinish.IsAvailable);
        Assert.Equal(13m, away.AttacksToFinish.AverageScorePerAttack);
        Assert.Equal(68, away.AttacksToFinish.RequiredAttacks);

        Assert.Collection(
            state.Holes,
            hole =>
            {
                Assert.Equal(WarHoleKind.IdleAttacker, hole.Kind);
                Assert.Equal(WarHoleSeverity.Critical, hole.Severity);
                Assert.Equal(111, hole.FactionId);
                Assert.Equal(1003, hole.MemberId);
            },
            hole =>
            {
                Assert.Equal(WarHoleKind.IdleAttacker, hole.Kind);
                Assert.Equal(WarHoleSeverity.High, hole.Severity);
                Assert.Equal(222, hole.FactionId);
                Assert.Equal(2002, hole.MemberId);
            },
            hole =>
            {
                Assert.Equal(WarHoleKind.OpenTarget, hole.Kind);
                Assert.Equal(WarHoleSeverity.Medium, hole.Severity);
                Assert.Equal(222, hole.FactionId);
                Assert.Equal(1002, hole.MemberId);
            });
    }

    [Fact]
    public void Derive_with_empty_roster_returns_empty_state_and_warning()
    {
        var samples = BuildFixtureSamples(48377);

        var state = new WarStateDerivationEngine().Derive([], samples, FixtureNowUtc);

        Assert.Equal(48377, state.WarId);
        Assert.Empty(state.Factions);
        Assert.Empty(state.Holes);
        Assert.Equal(1m, state.CoverageRatio);
        Assert.Contains("No roster snapshot rows were provided.", state.Warnings);
    }

    [Fact]
    public void Derive_with_single_score_sample_reports_insufficient_rate_data()
    {
        var report = DeserializeFixture<RankedWarReportResponse>("tests/fixtures/war/ranked-war-report-48377.json");
        var roster = MapRoster(report, FixtureCapturedAtUtc);
        var samples = BuildFixtureSamples(report.War.WarId).Take(1).ToArray();

        var state = new WarStateDerivationEngine().Derive(roster, samples, FixtureNowUtc, report.IdleAttackers);

        Assert.All(state.Factions, faction =>
        {
            Assert.False(faction.ScoreRate.IsAvailable);
            Assert.Equal("insufficient-score-samples", faction.ScoreRate.Diagnostic);
            Assert.False(faction.Eta.IsAvailable);
            Assert.Equal("insufficient-score-samples", faction.Eta.Diagnostic);
        });
        Assert.Contains(state.Warnings, warning => warning.Contains("does not have enough score samples", StringComparison.Ordinal));
    }

    [Fact]
    public void Derive_with_zero_score_delta_reports_eta_unavailable_without_throwing()
    {
        var report = DeserializeFixture<RankedWarReportResponse>("tests/fixtures/war/ranked-war-report-48377.json");
        var roster = MapRoster(report, FixtureCapturedAtUtc);
        var samples =
            BuildFixtureSamples(report.War.WarId)
                .Select(sample => new WarScoreSampleEntity
                {
                    Id = sample.Id,
                    WarId = sample.WarId,
                    FactionId = sample.FactionId,
                    FactionName = sample.FactionName,
                    FactionScore = 128,
                    FactionChain = sample.FactionChain,
                    OpponentFactionId = sample.OpponentFactionId,
                    OpponentFactionName = sample.OpponentFactionName,
                    OpponentScore = 117,
                    OpponentChain = sample.OpponentChain,
                    SampledAtUtc = sample.SampledAtUtc,
                })
                .ToArray();

        var state = new WarStateDerivationEngine().Derive(roster, samples, FixtureNowUtc, report.IdleAttackers);

        Assert.All(state.Factions, faction =>
        {
            Assert.False(faction.ScoreRate.IsAvailable);
            Assert.Equal("non-positive-score-delta", faction.ScoreRate.Diagnostic);
            Assert.False(faction.Eta.IsAvailable);
        });
    }

    [Fact]
    public void Derive_treats_elapsed_hospital_until_as_available_and_future_until_as_hospitalized()
    {
        var roster = new[]
        {
            new WarRosterSnapshotEntity
            {
                WarId = 48377,
                FactionId = 111,
                FactionName = "Happy Gym",
                MemberId = 1,
                MemberName = "Past Until",
                StatusState = "hospital",
                StatusUntilUtc = FixtureNowUtc.AddSeconds(-30),
                CapturedAtUtc = FixtureCapturedAtUtc,
            },
            new WarRosterSnapshotEntity
            {
                WarId = 48377,
                FactionId = 222,
                FactionName = "Chain Breakers",
                MemberId = 2,
                MemberName = "Future Until",
                StatusState = "hospital",
                StatusUntilUtc = FixtureNowUtc.AddSeconds(45),
                CapturedAtUtc = FixtureCapturedAtUtc,
            },
        };

        var state = new WarStateDerivationEngine().Derive(roster, [], FixtureNowUtc);
        var past = Assert.Single(state.Factions.Single(faction => faction.FactionId == 111).Members);
        var future = Assert.Single(state.Factions.Single(faction => faction.FactionId == 222).Members);

        Assert.Equal(WarMemberAvailabilityKind.Available, past.Availability);
        Assert.Equal(0, past.HospitalCountdownSeconds);
        Assert.Equal(WarMemberAvailabilityKind.Hospitalized, future.Availability);
        Assert.Equal(45, future.HospitalCountdownSeconds);
    }

    [Fact]
    public void Derive_warns_for_idle_attacker_ids_absent_from_roster()
    {
        var report = DeserializeFixture<RankedWarReportResponse>("tests/fixtures/war/ranked-war-report-48377.json");
        var roster = MapRoster(report, FixtureCapturedAtUtc);

        var state = new WarStateDerivationEngine().Derive(roster, BuildFixtureSamples(report.War.WarId), FixtureNowUtc, [9999]);

        Assert.Contains(state.Warnings, warning => warning.Contains("Idle attacker id 9999", StringComparison.Ordinal));
    }

    [Fact]
    public void Derive_with_no_available_opponent_targets_emits_only_idle_attacker_holes()
    {
        var report = DeserializeFixture<RankedWarReportResponse>("tests/fixtures/war/ranked-war-report-48377.json");
        var roster = MapRoster(report, FixtureCapturedAtUtc)
            .Select(row => row.MemberId == 1002
                ? new WarRosterSnapshotEntity
                {
                    WarId = row.WarId,
                    FactionId = row.FactionId,
                    FactionName = row.FactionName,
                    MemberId = row.MemberId,
                    MemberName = row.MemberName,
                    Score = row.Score,
                    Chain = row.Chain,
                    Attacks = row.Attacks,
                    StatusState = "travel",
                    StatusUntilUtc = row.StatusUntilUtc,
                    CapturedAtUtc = row.CapturedAtUtc,
                }
                : row)
            .ToArray();

        var state = new WarStateDerivationEngine().Derive(roster, BuildFixtureSamples(report.War.WarId), FixtureNowUtc, report.IdleAttackers);

        Assert.Equal(2, state.Holes.Count);
        Assert.DoesNotContain(state.Holes, hole => hole.Kind == WarHoleKind.OpenTarget);
    }

    private static T DeserializeFixture<T>(string relativePath)
    {
        var root = ResolveRepositoryRoot();
        var fullPath = Path.Combine(root, relativePath);
        var json = File.ReadAllText(fullPath);

        try
        {
            return JsonSerializer.Deserialize<T>(json, WarEndpointJson.SerializerOptions)
                ?? throw new XunitException($"Deserializer returned null for {typeof(T).Name}.");
        }
        catch (JsonException ex)
        {
            throw new XunitException($"Fixture '{relativePath}' failed to deserialize: {ex.Message}");
        }
    }

    private static WarRosterSnapshotEntity[] MapRoster(RankedWarReportResponse report, DateTimeOffset capturedAtUtc)
        => report.Factions
            .SelectMany(faction => faction.Members.Select(member => new WarRosterSnapshotEntity
            {
                WarId = report.War.WarId,
                FactionId = faction.FactionId,
                FactionName = faction.Name,
                MemberId = member.UserId,
                MemberName = member.Name,
                Score = member.Score,
                Chain = member.Chain,
                Attacks = member.Attacks,
                StatusState = member.Status?.State,
                StatusUntilUtc = member.Status?.Until,
                CapturedAtUtc = capturedAtUtc,
            }))
            .ToArray();

    private static WarScoreSampleEntity[] BuildFixtureSamples(long warId)
        =>
        [
            new WarScoreSampleEntity
            {
                Id = 1,
                WarId = warId,
                FactionId = 111,
                FactionName = "Happy Gym",
                FactionScore = 100,
                FactionChain = 30,
                OpponentFactionId = 222,
                OpponentFactionName = "Chain Breakers",
                OpponentScore = 90,
                OpponentChain = 27,
                SampledAtUtc = PriorSampleUtc,
            },
            new WarScoreSampleEntity
            {
                Id = 2,
                WarId = warId,
                FactionId = 111,
                FactionName = "Happy Gym",
                FactionScore = 128,
                FactionChain = 42,
                OpponentFactionId = 222,
                OpponentFactionName = "Chain Breakers",
                OpponentScore = 117,
                OpponentChain = 39,
                SampledAtUtc = FixtureCapturedAtUtc,
            },
        ];

    private static string ResolveRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HappyGymStats.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Unable to locate repository root from test output directory.");
    }
}
