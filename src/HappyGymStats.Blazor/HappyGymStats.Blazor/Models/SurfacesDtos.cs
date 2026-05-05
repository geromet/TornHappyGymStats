namespace HappyGymStats.Blazor.Models;

public sealed record SurfacesDatasetDto(
    string Dataset,
    string Version,
    DateTimeOffset SyncedAtUtc,
    SurfacesSeriesDto Series,
    SurfacesDatasetMetaDto Meta);

public sealed record SurfacesSeriesDto(
    GymCloudSeriesDto GymCloud,
    EventsCloudSeriesDto EventsCloud);

public sealed record GymCloudSeriesDto(
    double[] X,
    double[] Y,
    double[] Z,
    string[] Text,
    double[] Confidence,
    string[][] ConfidenceReasons,
    ProvenanceWarningDto[] ProvenanceWarnings);

public sealed record EventsCloudSeriesDto(
    double[] X,
    double[] Y,
    double[] Z,
    string[] Text);

public sealed record ProvenanceWarningDto(
    string LogId,
    string Scope,
    string VerificationStatus,
    string LinkTarget,
    int RowCount,
    bool HasManualOverride,
    string? ManualOverrideSource);

public sealed record SurfacesDatasetMetaDto(
    int GymPointCount,
    int EventPointCount,
    int RecordCount,
    ProvenanceDiagnosticsDto ProvenanceWarningsDiagnostics);

public sealed record ProvenanceDiagnosticsDto(
    int WarningCount,
    int SkippedMalformedRowCount,
    int OverrideLoadedEntryCount,
    int OverrideSkippedMalformedEntryCount,
    bool OverrideHitEntryCap,
    bool QueryFailed,
    string Reason);

public sealed record ImportStatusDto(
    string Id,
    string Outcome,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int PagesFetched,
    long LogsFetched,
    long LogsAppended,
    string? ErrorMessage);
