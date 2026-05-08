namespace HappyGymStats.Core.Torn.Models;

public sealed record UserLog(
    string Id,
    long Timestamp,
    string? Title,
    string? Category,
    int? LogTypeId,
    System.Text.Json.JsonElement Raw);
