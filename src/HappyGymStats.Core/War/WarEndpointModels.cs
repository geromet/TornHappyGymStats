using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HappyGymStats.Core.War;

public static class WarEndpointJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new NullableUnixSecondsDateTimeOffsetConverter(zeroMeansNull: true));
        options.Converters.Add(new UnixSecondsDateTimeOffsetConverter());
        return options;
    }
}

public sealed record LiveFactionWarsResponse
{
    [JsonPropertyName("wars")]
    public required IReadOnlyList<LiveFactionWar> Wars { get; init; }
}

public sealed record LiveFactionWar
{
    [JsonPropertyName("war_id")]
    public required long WarId { get; init; }

    [JsonPropertyName("faction_id")]
    public required long FactionId { get; init; }

    [JsonPropertyName("faction_name")]
    public required string FactionName { get; init; }

    [JsonPropertyName("opponent_id")]
    public required long OpponentId { get; init; }

    [JsonPropertyName("opponent_name")]
    public required string OpponentName { get; init; }

    [JsonPropertyName("start")]
    public DateTimeOffset? Start { get; init; }

    [JsonPropertyName("end")]
    public DateTimeOffset? End { get; init; }

    [JsonPropertyName("is_live")]
    public bool IsLive { get; init; }

    [JsonPropertyName("score")]
    public int? Score { get; init; }

    [JsonPropertyName("chain")]
    public int? Chain { get; init; }
}

public sealed record RankedWarReportResponse
{
    [JsonPropertyName("war")]
    public required RankedWarSummary War { get; init; }

    [JsonPropertyName("factions")]
    public required IReadOnlyList<RankedWarFactionReport> Factions { get; init; }

    [JsonPropertyName("idle_attackers")]
    public IReadOnlyList<long> IdleAttackers { get; init; } = Array.Empty<long>();
}

public sealed record RankedWarSummary
{
    [JsonPropertyName("war_id")]
    public required long WarId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("start")]
    public required DateTimeOffset Start { get; init; }

    [JsonPropertyName("end")]
    public DateTimeOffset? End { get; init; }

    [JsonPropertyName("is_live")]
    public bool IsLive { get; init; }
}

public sealed record RankedWarFactionReport
{
    [JsonPropertyName("faction_id")]
    public required long FactionId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("score")]
    public required int Score { get; init; }

    [JsonPropertyName("chain")]
    public required int Chain { get; init; }

    [JsonPropertyName("members")]
    public IReadOnlyList<RankedWarMemberReport> Members { get; init; } = Array.Empty<RankedWarMemberReport>();
}

public sealed record RankedWarMemberReport
{
    [JsonPropertyName("user_id")]
    public required long UserId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("score")]
    public int Score { get; init; }

    [JsonPropertyName("chain")]
    public int Chain { get; init; }

    [JsonPropertyName("attacks")]
    public int Attacks { get; init; }

    [JsonPropertyName("status")]
    public WarMemberStatus? Status { get; init; }
}

public sealed record WarMemberStatus
{
    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("until")]
    public DateTimeOffset? Until { get; init; }
}

public sealed record GlobalRankedWarsResponse
{
    [JsonPropertyName("wars")]
    public required IReadOnlyList<GlobalRankedWar> Wars { get; init; }
}

public sealed record GlobalRankedWar
{
    [JsonPropertyName("war_id")]
    public required long WarId { get; init; }

    [JsonPropertyName("faction_id")]
    public required long FactionId { get; init; }

    [JsonPropertyName("opponent_id")]
    public required long OpponentId { get; init; }

    [JsonPropertyName("start")]
    public required DateTimeOffset Start { get; init; }

    [JsonPropertyName("end")]
    public DateTimeOffset? End { get; init; }

    [JsonPropertyName("winner_faction_id")]
    public long? WinnerFactionId { get; init; }

    [JsonPropertyName("is_live")]
    public bool IsLive => End is null;
}

public sealed record UserAttacksPageResponse
{
    [JsonPropertyName("attacks")]
    public required IReadOnlyList<UserAttackEntry> Attacks { get; init; }

    [JsonPropertyName("_metadata")]
    public UserAttacksPageMetadata? Metadata { get; init; }
}

public sealed record UserAttackEntry
{
    [JsonPropertyName("attack_id")]
    public required long AttackId { get; init; }

    [JsonPropertyName("war_id")]
    public long? WarId { get; init; }

    [JsonPropertyName("timestamp")]
    public required DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("attacker_id")]
    public required long AttackerId { get; init; }

    [JsonPropertyName("attacker_name")]
    public required string AttackerName { get; init; }

    [JsonPropertyName("defender_id")]
    public required long DefenderId { get; init; }

    [JsonPropertyName("defender_name")]
    public required string DefenderName { get; init; }

    [JsonPropertyName("result")]
    public required string Result { get; init; }

    [JsonPropertyName("respect_gain")]
    public decimal? RespectGain { get; init; }

    [JsonPropertyName("chain")]
    public int? Chain { get; init; }

    [JsonPropertyName("is_ranked_war")]
    public bool IsRankedWar { get; init; }
}

public sealed record UserAttacksPageMetadata
{
    [JsonPropertyName("links")]
    public UserAttacksPageLinks? Links { get; init; }
}

public sealed record UserAttacksPageLinks
{
    [JsonPropertyName("next")]
    public string? Next { get; init; }
}

public sealed record FactionSnapshotHandoff
{
    [JsonPropertyName("faction_id")]
    public required long FactionId { get; init; }

    [JsonPropertyName("faction_name")]
    public required string FactionName { get; init; }

    [JsonPropertyName("captured_at")]
    public required DateTimeOffset CapturedAt { get; init; }

    [JsonPropertyName("csv_name")]
    public required string CsvName { get; init; }
}

internal sealed class NullableUnixSecondsDateTimeOffsetConverter(bool zeroMeansNull) : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var seconds = reader.TokenType switch
        {
            JsonTokenType.Number when reader.TryGetInt64(out var numeric) => numeric,
            JsonTokenType.String => ParseStringSeconds(reader.GetString()),
            _ => throw new JsonException($"Expected unix timestamp or null but found {reader.TokenType}.")
        };

        if (zeroMeansNull && seconds == 0)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteNumberValue(value.Value.ToUnixTimeSeconds());
    }

    private static long ParseStringSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            throw new JsonException($"Invalid unix timestamp '{value ?? string.Empty}'.");
        }

        return seconds;
    }
}

internal sealed class UnixSecondsDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return new NullableUnixSecondsDateTimeOffsetConverter(zeroMeansNull: false).Read(ref reader, typeof(DateTimeOffset?), options)
            ?? throw new JsonException("Expected required unix timestamp value.");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.ToUnixTimeSeconds());
    }
}
