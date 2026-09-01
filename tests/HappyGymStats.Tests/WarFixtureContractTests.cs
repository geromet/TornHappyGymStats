using System.Text.Json;
using HappyGymStats.Core.War;
using Xunit;
using Xunit.Sdk;

namespace HappyGymStats.Tests;

public sealed class WarFixtureContractTests
{
    [Fact]
    public void LiveFactionWars_fixture_deserializes_required_fields_and_preserves_live_end_null()
    {
        var payload = DeserializeFixture<LiveFactionWarsResponse>("tests/fixtures/war/live-faction-wars.json");

        Assert.Equal(2, payload.Wars.Count);

        var liveWar = payload.Wars[0];
        Assert.Equal(48377, liveWar.WarId);
        Assert.Equal(111, liveWar.FactionId);
        Assert.Equal(222, liveWar.OpponentId);
        Assert.True(liveWar.IsLive);
        Assert.Equal(128, liveWar.Score);
        Assert.Equal(42, liveWar.Chain);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1731000000), liveWar.Start);
        Assert.Null(liveWar.End);

        var closedWar = payload.Wars[1];
        Assert.False(closedWar.IsLive);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1730907200), closedWar.End);
    }

    [Fact]
    public void RankedWarReport_fixture_deserializes_rosters_scores_status_and_idle_attackers()
    {
        var payload = DeserializeFixture<RankedWarReportResponse>("tests/fixtures/war/ranked-war-report-48377.json");

        Assert.Equal(48377, payload.War.WarId);
        Assert.True(payload.War.IsLive);
        Assert.Null(payload.War.End);
        Assert.Equal(2, payload.Factions.Count);
        Assert.Equal(new long[] { 1003, 2002 }, payload.IdleAttackers);

        var home = payload.Factions[0];
        Assert.Equal(111, home.FactionId);
        Assert.Equal("Happy Gym", home.Name);
        Assert.Equal(128, home.Score);
        Assert.Equal(42, home.Chain);
        Assert.Equal(3, home.Members.Count);

        var hospitalMember = home.Members[0];
        Assert.Equal(1001, hospitalMember.UserId);
        Assert.Equal("hospital", hospitalMember.Status?.State);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1731003600), hospitalMember.Status?.Until);

        var idleMember = home.Members[2];
        Assert.Equal("idle", idleMember.Status?.State);
        Assert.Null(idleMember.Status?.Until);

        var awayHospitalMember = payload.Factions[1].Members[0];
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1731005400), awayHospitalMember.Status?.Until);
    }

    [Fact]
    public void GlobalRankedWars_fixture_normalizes_live_end_from_null_and_zero()
    {
        var payload = DeserializeFixture<GlobalRankedWarsResponse>("tests/fixtures/war/global-ranked-wars-live.json");

        Assert.Equal(3, payload.Wars.Count);

        Assert.True(payload.Wars[0].IsLive);
        Assert.Null(payload.Wars[0].End);

        Assert.True(payload.Wars[1].IsLive);
        Assert.Null(payload.Wars[1].End);

        Assert.False(payload.Wars[2].IsLive);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1730807200), payload.Wars[2].End);
        Assert.Equal(666, payload.Wars[2].WinnerFactionId);
    }

    [Fact]
    public void UserAttacks_fixture_deserializes_multi_attack_page_and_metadata_link()
    {
        var payload = DeserializeFixture<UserAttacksPageResponse>("tests/fixtures/war/user-attacks-page.json");

        Assert.Equal(3, payload.Attacks.Count);
        Assert.Equal("/user/?selections=attacks&page=2", payload.Metadata?.Links?.Next);

        var firstAttack = payload.Attacks[0];
        Assert.Equal(90001, firstAttack.AttackId);
        Assert.Equal(48377, firstAttack.WarId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1731000200), firstAttack.Timestamp);
        Assert.Equal(3.45m, firstAttack.RespectGain);
        Assert.Equal(21, firstAttack.Chain);
        Assert.True(firstAttack.IsRankedWar);

        var nonWarAttack = payload.Attacks[2];
        Assert.Null(nonWarAttack.WarId);
        Assert.False(nonWarAttack.IsRankedWar);
        Assert.Equal(0m, nonWarAttack.RespectGain);
        Assert.Equal(0, nonWarAttack.Chain);
    }

    [Fact]
    public void Plaintext_models_do_not_include_api_keys_or_full_request_urls()
    {
        var report = DeserializeFixture<RankedWarReportResponse>("tests/fixtures/war/ranked-war-report-48377.json");
        var attacks = DeserializeFixture<UserAttacksPageResponse>("tests/fixtures/war/user-attacks-page.json");

        var reportText = JsonSerializer.Serialize(report, WarEndpointJson.SerializerOptions);
        var attackText = JsonSerializer.Serialize(attacks, WarEndpointJson.SerializerOptions);

        Assert.DoesNotContain("key=", reportText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("key=", attackText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIMITED-KEY", reportText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LIMITED-KEY", attackText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://api.torn.com", reportText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://api.torn.com", attackText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_required_war_id_fails_deserialization()
    {
        const string json = """
        {
          "wars": [
            {
              "faction_id": 111,
              "faction_name": "Happy Gym",
              "opponent_id": 222,
              "opponent_name": "Chain Breakers"
            }
          ]
        }
        """;

        Assert.Throws<JsonException>(() => Deserialize<LiveFactionWarsResponse>(json));
    }

    [Fact]
    public void Missing_required_faction_id_fails_deserialization()
    {
        const string json = """
        {
          "war": {
            "war_id": 48377,
            "start": 1731000000,
            "end": null,
            "is_live": true
          },
          "factions": [
            {
              "name": "Happy Gym",
              "score": 1,
              "chain": 2,
              "members": []
            }
          ]
        }
        """;

        Assert.Throws<JsonException>(() => Deserialize<RankedWarReportResponse>(json));
    }

    [Fact]
    public void Empty_roster_and_attack_arrays_are_allowed()
    {
        const string rankedWarJson = """
        {
          "war": {
            "war_id": 48377,
            "start": 1731000000,
            "end": null,
            "is_live": true
          },
          "factions": [
            {
              "faction_id": 111,
              "name": "Happy Gym",
              "score": 0,
              "chain": 0,
              "members": []
            }
          ],
          "idle_attackers": []
        }
        """;

        const string attacksJson = """
        {
          "attacks": [],
          "_metadata": {
            "links": {
              "next": null
            }
          }
        }
        """;

        var rankedWar = Deserialize<RankedWarReportResponse>(rankedWarJson);
        var attacks = Deserialize<UserAttacksPageResponse>(attacksJson);

        Assert.Empty(rankedWar.Factions[0].Members);
        Assert.Empty(rankedWar.IdleAttackers);
        Assert.Empty(attacks.Attacks);
        Assert.Null(attacks.Metadata?.Links?.Next);
    }

    [Fact]
    public void Malformed_status_until_value_fails_deserialization()
    {
        const string json = """
        {
          "war": {
            "war_id": 48377,
            "start": 1731000000,
            "end": null,
            "is_live": true
          },
          "factions": [
            {
              "faction_id": 111,
              "name": "Happy Gym",
              "score": 1,
              "chain": 2,
              "members": [
                {
                  "user_id": 1001,
                  "name": "Alice",
                  "status": {
                    "state": "hospital",
                    "until": "tomorrow"
                  }
                }
              ]
            }
          ]
        }
        """;

        Assert.Throws<JsonException>(() => Deserialize<RankedWarReportResponse>(json));
    }

    private static T DeserializeFixture<T>(string relativePath)
    {
        var root = ResolveRepositoryRoot();
        var fullPath = Path.Combine(root, relativePath);
        var json = File.ReadAllText(fullPath);

        try
        {
            return Deserialize<T>(json);
        }
        catch (JsonException ex)
        {
            throw new XunitException($"Fixture '{relativePath}' failed to deserialize: {ex.Message}");
        }
    }

    private static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, WarEndpointJson.SerializerOptions)
            ?? throw new XunitException($"Deserializer returned null for {typeof(T).Name}.");
    }

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
