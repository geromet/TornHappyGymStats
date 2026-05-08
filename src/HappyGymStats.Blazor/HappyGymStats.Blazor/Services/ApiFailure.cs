using System.Net;

namespace HappyGymStats.Blazor.Services;

public enum ApiFailureCategory
{
    ApiUnavailable,
    BadGateway,
    NotFound,
    Validation,
    ImportFailure,
    HttpFailure,
    Deserialization
}

public sealed class ApiFailure : Exception
{
    public string Endpoint { get; }
    public HttpStatusCode? StatusCode { get; }
    public ApiFailureCategory Category { get; }
    public string SafeMessage { get; }

    public ApiFailure(
        string endpoint,
        ApiFailureCategory category,
        string safeMessage,
        HttpStatusCode? statusCode = null,
        Exception? innerException = null) : base(safeMessage, innerException)
    {
        Endpoint = endpoint;
        Category = category;
        SafeMessage = safeMessage;
        StatusCode = statusCode;
    }

    public static ApiFailure FromHttp(string endpoint, HttpStatusCode statusCode)
    {
        var (category, safeMessage) = statusCode switch
        {
            HttpStatusCode.BadGateway => (ApiFailureCategory.BadGateway, "The API gateway returned 502 Bad Gateway."),
            HttpStatusCode.NotFound => (ApiFailureCategory.NotFound, "Requested data was not found."),
            HttpStatusCode.BadRequest => (ApiFailureCategory.Validation, "The request was rejected as invalid."),
            HttpStatusCode.UnprocessableEntity => (ApiFailureCategory.Validation, "The request failed validation."),
            HttpStatusCode.ServiceUnavailable => (ApiFailureCategory.ApiUnavailable, "The API service is currently unavailable."),
            _ when (int)statusCode >= 500 => (ApiFailureCategory.HttpFailure, $"The API request failed with status {(int)statusCode}."),
            _ when (int)statusCode >= 400 => (ApiFailureCategory.Validation, $"The API rejected the request with status {(int)statusCode}."),
            _ => (ApiFailureCategory.HttpFailure, $"The API request failed with status {(int)statusCode}.")
        };

        return new ApiFailure(endpoint, category, safeMessage, statusCode);
    }

    public static ApiFailure Deserialization(string endpoint, Exception innerException) =>
        new(endpoint, ApiFailureCategory.Deserialization, "The API response payload was malformed or unexpected.", null, innerException);
}
