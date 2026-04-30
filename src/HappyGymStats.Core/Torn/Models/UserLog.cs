namespace HappyGymStats.Torn.Models;

public sealed record UserLog(
    string Id,
    long Timestamp,
    string? Title,
    string? Category,
    System.Text.Json.JsonElement Raw);
