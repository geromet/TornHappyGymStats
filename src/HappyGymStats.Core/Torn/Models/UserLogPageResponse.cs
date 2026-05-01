using System.Text.Json;
using System.Text.Json.Serialization;

namespace HappyGymStats.Core.Torn.Models;

public sealed record UserLogPageResponse
{
    [JsonPropertyName("logs")]
    public JsonElement Logs { get; init; }

    [JsonPropertyName("_metadata")]
    public UserLogPageMetadata? Metadata { get; init; }
}

public sealed record UserLogPageMetadata
{
    [JsonPropertyName("links")]
    public UserLogPageLinks? Links { get; init; }
}

public sealed record UserLogPageLinks
{
    [JsonPropertyName("next")]
    public string? Next { get; init; }
}
