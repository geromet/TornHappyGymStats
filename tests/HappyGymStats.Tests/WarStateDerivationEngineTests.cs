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

        // Faction 111 has two available members (1002 not-idle, 1003 idle), so from faction 222's
        // side both are open slots - the idle one included. Faction 111 sees no open slots because
        // faction 222 has zero available members (one hospital, one abroad).
        Assert.Equal(0, home.OpenTargetCount);
        Assert.Equal(0m, home.TargetCoverageRatio);
        Assert.Equal(2, away.OpenTargetCount);
        Assert.Equal(0m, away.TargetCoverageRatio); // 0 available attackers => 0 coverage, not 200%
        Assert.Equal(2, state.OpenTargetCount);

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
            },
            hole =>
            {
                Assert.Equal(WarHoleKind.OpenTarget, hole.Kind);
                Assert.Equal(WarHoleSeverity.Medium, hole.Severity);
                Assert.Equal(222, hole.FactionId);
                Assert.Equal(1003, hole.MemberId);
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
    public void Derive_with_no_available_members_on_either_side_emits_only_idle_attacker_holes()
    {
        var report = DeserializeFixture<RankedWarReportResponse>("tests/fixtures/war/ranked-war-report-48377.json");
        // Send faction 111's only two available members (1002, 1003) abroad, so neither faction
        // has an attackable target left.
        var roster = MapRoster(report, FixtureCapturedAtUtc)
            .Select(row => row.MemberId is 1002 or 1003
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
        Assert.Equal(0, state.OpenTargetCount);
    }

    [Fact]
    public void Derive_emits_open_target_holes_for_attackable_enemies_even_when_our_side_has_no_idlers()
    {
        // No idle attackers anywhere. Faction 1 has two available members; faction 2 has one
        // available plus one hospitalised and one abroad.
        var roster = new[]
        {
            Roster(1, 10, "A1", "okay"),
            Roster(1, 11, "A2", "okay"),
            Roster(2, 20, "B1", "okay"),
            Roster(2, 21, "B2", "hospital", FixtureNowUtc.AddMinutes(20)),
            Roster(2, 22, "B3", "abroad"),
        };

        var state = new WarStateDerivationEngine().Derive(roster, [], FixtureNowUtc);

        var f1 = state.Factions.Single(f => f.FactionId == 1);
        var f2 = state.Factions.Single(f => f.FactionId == 2);

        Assert.Empty(state.Holes.Where(h => h.Kind == WarHoleKind.IdleAttacker));
        // Faction 1 can hit B1 only; faction 2 can hit A1 and A2.
        Assert.Equal(1, f1.OpenTargetCount);
        Assert.Equal(2, f2.OpenTargetCount);
        Assert.Equal(3, state.OpenTargetCount);
        Assert.Equal(WarHoleKind.OpenTarget, Assert.Single(state.Holes, h => h.FactionId == 1).Kind);
        Assert.All(state.Holes.Where(h => h.FactionId == 2), h => Assert.Equal(WarHoleKind.OpenTarget, h.Kind));
        // A hospitalised / abroad enemy is a regenerating slot, not an open target.
        Assert.DoesNotContain(state.Holes, h => h.MemberId is 21 or 22);
    }

    [Fact]
    public void Derive_treats_an_idle_enemy_as_an_open_target()
    {
        var roster = new[]
        {
            Roster(1, 10, "A1", "okay"),
            Roster(2, 20, "B1", "idle"),
        };

        var state = new WarStateDerivationEngine().Derive(roster, [], FixtureNowUtc);

        var openTarget = Assert.Single(state.Holes, h => h.Kind == WarHoleKind.OpenTarget && h.FactionId == 1);
        Assert.Equal(20, openTarget.MemberId);
        Assert.Contains("idle", openTarget.Reason, StringComparison.OrdinalIgnoreCase);
        // The same member is also faction 2's idle-attacker hole - both are first-class.
        Assert.Contains(state.Holes, h => h.Kind == WarHoleKind.IdleAttacker && h.FactionId == 2 && h.MemberId == 20);
    }

    [Fact]
    public void Derive_target_coverage_ratio_is_attackable_enemies_over_available_attackers()
    {
        var roster = new[]
        {
            Roster(1, 10, "A1", "okay"),
            Roster(1, 11, "A2", "okay"),
            Roster(1, 12, "A3", "okay"),
            Roster(1, 13, "A4", "okay"),
            Roster(2, 20, "B1", "okay"),
            Roster(2, 21, "B2", "okay"),
        };

        var state = new WarStateDerivationEngine().Derive(roster, [], FixtureNowUtc);

        var f1 = state.Factions.Single(f => f.FactionId == 1);
        // 2 attackable enemies over 4 available attackers = 0.5.
        Assert.Equal(2, f1.OpenTargetCount);
        Assert.Equal(4, f1.AvailableMemberCount);
        Assert.Equal(0.5m, f1.TargetCoverageRatio);
    }

    [Fact]
    public void Derive_target_coverage_ratio_is_zero_when_a_faction_has_no_available_attacker()
    {
        var roster = new[]
        {
            Roster(1, 10, "A1", "hospital", FixtureNowUtc.AddMinutes(15)),
            Roster(1, 11, "A2", "abroad"),
            Roster(2, 20, "B1", "okay"),
            Roster(2, 21, "B2", "okay"),
        };

        var state = new WarStateDerivationEngine().Derive(roster, [], FixtureNowUtc);
        var f1 = state.Factions.Single(f => f.FactionId == 1);

        Assert.Equal(0, f1.AvailableMemberCount);
        Assert.Equal(2, f1.OpenTargetCount); // both enemies are attackable
        Assert.Equal(0m, f1.TargetCoverageRatio); // ...but this faction can cover none of them
    }

    [Fact]
    public void Derive_attaches_chain_command_with_an_inferred_lapse_timer()
    {
        var roster = new[]
        {
            Roster(1, 10, "A1", "okay"),
            Roster(1, 11, "A2", "okay"),
            Roster(2, 20, "B1", "okay"),
            Roster(2, 21, "B2", "okay"),
        };
        var samples = new[]
        {
            ChainSample(id: 1, factionChain: 8, at: FixtureNowUtc.AddSeconds(-30)),
            ChainSample(id: 2, factionChain: 9, at: FixtureNowUtc),
        };

        var state = new WarStateDerivationEngine().Derive(roster, samples, FixtureNowUtc);
        var f1 = state.Factions.Single(f => f.FactionId == 1);

        Assert.NotNull(f1.ChainState);
        Assert.Equal(9, f1.ChainState!.ChainLength);
        Assert.Equal(10, f1.ChainState.NextMilestone);
        Assert.Equal(ChainBoardMode.WarTargetsOnly, f1.ChainState.Mode); // in window, enemies attackable

        Assert.NotNull(f1.ChainTimer);
        Assert.Equal(ChainLapseConfidence.Inferred, f1.ChainTimer!.Confidence);
        Assert.Equal(0, f1.ChainTimer.SecondsSinceLastIncrease); // chain rose at "now"
        Assert.Equal(30, f1.ChainTimer.SampleSpacingSeconds);

        Assert.Equal(ChainAlertLevel.ReservationWindow, f1.ChainAlert);

        // The opponent card carries no chain command - advice like "wait or revive" is addressed
        // to us, and an enemy chain nearing lapse must not paint as our red alert.
        var f2 = state.Factions.Single(f => f.FactionId == 2);
        Assert.Null(f2.ChainState);
        Assert.Null(f2.ChainTimer);
        Assert.Equal(ChainAlertLevel.None, f2.ChainAlert);
    }

    [Fact]
    public void Derive_chain_command_holds_for_a_war_target_when_none_is_attackable_in_the_window()
    {
        // data/V2/handoff/06 S07 acceptance, at the board's data layer: chain in the reservation
        // window with nothing attackable -> advise waiting, name the forfeited milestone bonus.
        var roster = new[]
        {
            Roster(1, 10, "A1", "okay"),
            Roster(2, 20, "B1", "hospital", FixtureNowUtc.AddMinutes(30)),
            Roster(2, 21, "B2", "abroad"),
        };
        var samples = new[]
        {
            ChainSample(id: 1, factionChain: 996, at: FixtureNowUtc.AddSeconds(-30)),
            ChainSample(id: 2, factionChain: 997, at: FixtureNowUtc),
        };

        var state = new WarStateDerivationEngine().Derive(roster, samples, FixtureNowUtc);
        var f1 = state.Factions.Single(f => f.FactionId == 1);

        Assert.Equal(0, f1.ChainState!.AttackableWarTargetCount);
        Assert.Equal(ChainBoardMode.HoldForWarTarget, f1.ChainState.Mode);
        Assert.Contains("Wait or revive", f1.ChainState.Reason);
        Assert.Contains("640", f1.ChainState.Reason);
        Assert.Equal(1000, f1.ChainState.NextMilestone);
    }

    [Fact]
    public void Derive_prefers_Torns_own_deadline_over_the_inference()
    {
        var roster = new[]
        {
            Roster(1, 10, "A1", "okay"),
            Roster(1, 11, "A2", "okay"),
            Roster(2, 20, "B1", "okay"),
            Roster(2, 21, "B2", "okay"),
        };
        var samples = new[]
        {
            ChainSample(id: 1, factionChain: 8, at: FixtureNowUtc.AddSeconds(-30)),
            ChainSample(id: 2, factionChain: 9, at: FixtureNowUtc, lapsesAtUtc: FixtureNowUtc.AddSeconds(263)),
        };

        var state = new WarStateDerivationEngine().Derive(roster, samples, FixtureNowUtc);
        var ours = state.Factions.Single(f => f.FactionId == 1);

        Assert.Equal(ChainLapseConfidence.Exact, ours.ChainTimer!.Confidence);
        Assert.Equal(263, ours.ChainTimer.SecondsUntilLapse);
        Assert.False(ours.ChainTimer.IsInferred);
        Assert.Equal(0, ours.ChainTimer.SampleSpacingSeconds);
        Assert.Equal(FixtureNowUtc.AddSeconds(263), ours.ChainTimer.LapsesAtUtc);
    }

    [Fact]
    public void Derive_falls_back_to_inference_when_the_newest_deadline_has_already_passed()
    {
        // Torn stops reporting a chain the moment it lapses, so a deadline in the past means
        // our newest sample predates the lapse. Reporting an exact "0 seconds left" from it
        // would assert the chain is alive and about to expire when it is already gone.
        var roster = new[]
        {
            Roster(1, 10, "A1", "okay"),
            Roster(1, 11, "A2", "okay"),
            Roster(2, 20, "B1", "okay"),
            Roster(2, 21, "B2", "okay"),
        };
        var samples = new[]
        {
            ChainSample(id: 1, factionChain: 8, at: FixtureNowUtc.AddSeconds(-30)),
            ChainSample(id: 2, factionChain: 9, at: FixtureNowUtc, lapsesAtUtc: FixtureNowUtc.AddSeconds(-5)),
        };

        var state = new WarStateDerivationEngine().Derive(roster, samples, FixtureNowUtc);
        var ours = state.Factions.Single(f => f.FactionId == 1);

        Assert.Equal(ChainLapseConfidence.Inferred, ours.ChainTimer!.Confidence);
    }

    [Fact]
    public void Derive_never_gives_the_enemy_an_exact_timer()
    {
        // The chain selection reports the chain of the faction the key belongs to, so an
        // enemy deadline cannot exist. The enemy card carries no chain command at all.
        var roster = new[]
        {
            Roster(1, 10, "A1", "okay"),
            Roster(1, 11, "A2", "okay"),
            Roster(2, 20, "B1", "okay"),
            Roster(2, 21, "B2", "okay"),
        };
        var samples = new[]
        {
            ChainSample(id: 1, factionChain: 9, at: FixtureNowUtc, lapsesAtUtc: FixtureNowUtc.AddSeconds(263)),
        };

        var state = new WarStateDerivationEngine().Derive(roster, samples, FixtureNowUtc);
        var enemy = state.Factions.Single(f => f.FactionId == 2);

        Assert.Null(enemy.ChainTimer);
    }

    private static WarScoreSampleEntity ChainSample(
        long id,
        int factionChain,
        DateTimeOffset at,
        DateTimeOffset? lapsesAtUtc = null)
        => new()
        {
            FactionChainLapsesAtUtc = lapsesAtUtc,
            Id = id,
            WarId = 48377,
            FactionId = 1,
            FactionName = "Alpha",
            FactionScore = 100 + (int)id,
            FactionChain = factionChain,
            OpponentFactionId = 2,
            OpponentFactionName = "Bravo",
            OpponentScore = 90,
            OpponentChain = 5,
            SampledAtUtc = at,
        };

    private static WarRosterSnapshotEntity Roster(
        long factionId, long memberId, string name, string state, DateTimeOffset? until = null)
        => new()
        {
            WarId = 48377,
            FactionId = factionId,
            FactionName = factionId == 1 ? "Alpha" : "Bravo",
            MemberId = memberId,
            MemberName = name,
            Score = 10,
            Chain = 0,
            Attacks = 1,
            StatusState = state,
            StatusUntilUtc = until,
            CapturedAtUtc = FixtureCapturedAtUtc,
        };

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
