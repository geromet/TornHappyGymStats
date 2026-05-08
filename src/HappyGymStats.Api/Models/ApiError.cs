namespace HappyGymStats.Api;

public sealed record ApiError(string Code, string Message, object? Details, string RequestId);

public sealed record ErrorEnvelope(ApiError Error);
