namespace HappyGymStats.Core.Models;

/// <summary>
/// Surfaces-cache read models consumed by the Blazor frontend (single definition).
/// The cache payload itself is written by <c>SurfacesCacheWriter</c>; these records
/// cover the subset the frontend renders (extra JSON fields are ignored on read).
/// </summary>
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

public sealed record MyStatsDatasetDto(
    string Dataset,
    string Version,
    MyStatsSeriesDto Series,
    MyStatsMetaDto Meta);

public sealed record MyStatsSeriesDto(
    GymCloudSeriesDto GymCloud);

public sealed record MyStatsMetaDto(
    int GymPointCount,
    int RecordCount);