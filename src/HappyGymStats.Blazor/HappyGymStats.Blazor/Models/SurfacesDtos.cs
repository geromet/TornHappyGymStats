namespace HappyGymStats.Blazor.Models;

public sealed record SurfacesDatasetDto(
    string Dataset,
    string Version,
    SurfacesSeriesDto Series,
    SurfacesDatasetMetaDto Meta);

public sealed record SurfacesSeriesDto(
    GymCloudSeriesDto GymCloud,
    EventsCloudSeriesDto EventsCloud);

public sealed record GymCloudSeriesDto(
    double[] X,
    double[] Y,
    double[] Z);

public sealed record EventsCloudSeriesDto(
    double[] X,
    double[] Y,
    double[] Z);

public sealed record SurfacesDatasetMetaDto(
    int GymPointCount,
    int EventPointCount,
    int RecordCount);

public sealed record ImportStatusDto(
    string Id,
    string Outcome,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int PagesFetched,
    long LogsFetched,
    long LogsAppended,
    string? ErrorMessage);
