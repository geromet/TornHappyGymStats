using System.Net.Http.Headers;
using System.Net.Http.Json;
using HappyGymStats.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace HappyGymStats.Blazor.Services;

public sealed class WarBoardService(
    HttpClient http,
    IServerAccessTokenProvider accessTokenProvider,
    ILogger<WarBoardService> logger) : IAsyncDisposable
{
    private const string CurrentEndpoint = "/api/v1/war/current";
    private const string HubEndpoint = "/api/hub/war";
    private const string HubEventName = "WarStateUpdated";
    private const string HubRequestMethod = "RequestCurrentState";

    private readonly SemaphoreSlim initializeGate = new(1, 1);
    private HubConnection? hubConnection;
    private bool initialized;

    public event Action? StateChanged;

    public WarStateDto? CurrentState { get; private set; }
    public ApiFailure? CurrentFailure { get; private set; }
    public string ConnectionState { get; private set; } = "disconnected";
    public string DataSource { get; private set; } = "none";
    public DateTimeOffset? LastHubMessageAtUtc { get; private set; }
    public string? ConnectionError { get; private set; }
    public bool IsLoading { get; private set; }
    public bool HasError => CurrentFailure is not null || (CurrentState?.Errors.Count ?? 0) > 0;
    /// <summary>True when what the board shows may not reflect the live war.</summary>
    /// <remarks>
    /// Guarded on there actually being a war. `IsReady == false` covers both
    /// "no war running" and "degraded", and until the API stopped reporting the
    /// former as a 503 it could only ever mean the latter. Without the guard,
    /// every quiet evening raised "Stale data. Review heartbeat, warnings, and
    /// hub connection status before acting on roster gaps" — over a board with
    /// no heartbeat to review and no roster to have gaps in.
    /// </remarks>
    public bool HasStaleData =>
        !HasNoActiveWar &&
        (CurrentState?.Heartbeat.IsStale == true ||
         CurrentState?.IsReady == false ||
         ContainsOperationalWarning(CurrentState?.Warnings) ||
         !string.IsNullOrWhiteSpace(ConnectionError));

    /// <summary>True when the API reports no war in progress — a normal state, not a fault.</summary>
    public bool HasNoActiveWar => CurrentState?.Status == WarStatus.NotReady;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (initialized)
        {
            return;
        }

        await initializeGate.WaitAsync(ct);
        try
        {
            if (initialized)
            {
                return;
            }

            IsLoading = true;
            NotifyStateChanged();

            await LoadCurrentStateAsync(ct);
            await EnsureHubStartedAsync(ct);

            initialized = true;
        }
        finally
        {
            IsLoading = false;
            initializeGate.Release();
            NotifyStateChanged();
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        NotifyStateChanged();

        try
        {
            await LoadCurrentStateAsync(ct);
            if (hubConnection is { State: HubConnectionState.Disconnected })
            {
                await EnsureHubStartedAsync(ct);
            }
        }
        finally
        {
            IsLoading = false;
            NotifyStateChanged();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (hubConnection is null)
        {
            initializeGate.Dispose();
            return;
        }

        await hubConnection.DisposeAsync();
        initializeGate.Dispose();
    }

    private async Task LoadCurrentStateAsync(CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            await ApplyAccessTokenAsync();
            response = await http.GetAsync(CurrentEndpoint, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "War board bootstrap request failed for {Endpoint}", CurrentEndpoint);
            CurrentFailure = new ApiFailure(CurrentEndpoint, ApiFailureCategory.ApiUnavailable, "The war board could not reach the API service.", null, ex);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            CurrentFailure = ApiFailure.FromHttp(CurrentEndpoint, response.StatusCode);
            logger.LogInformation("War board bootstrap returned {StatusCode} for {Endpoint}", (int)response.StatusCode, CurrentEndpoint);
            return;
        }

        try
        {
            var state = await response.Content.ReadFromJsonAsync<WarStateDto>(cancellationToken: ct);
            CurrentFailure = state is null
                ? ApiFailure.Deserialization(CurrentEndpoint, new InvalidOperationException("The current war state payload was empty."))
                : null;

            if (state is not null)
            {
                ApplyState(state, "bootstrap");
            }
        }
        catch (NotSupportedException ex)
        {
            CurrentFailure = ApiFailure.Deserialization(CurrentEndpoint, ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            CurrentFailure = ApiFailure.Deserialization(CurrentEndpoint, ex);
        }
    }

    private async Task EnsureHubStartedAsync(CancellationToken ct)
    {
        hubConnection ??= BuildHubConnection();

        if (hubConnection.State is HubConnectionState.Connected or HubConnectionState.Connecting or HubConnectionState.Reconnecting)
        {
            return;
        }

        try
        {
            ConnectionError = null;
            ConnectionState = "connecting";
            NotifyStateChanged();

            await hubConnection.StartAsync(ct);
            ConnectionState = hubConnection.State.ToString().ToLowerInvariant();
            await hubConnection.InvokeAsync(HubRequestMethod, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or HubException or InvalidOperationException or TimeoutException or OperationCanceledException)
        {
            ConnectionError = $"Hub connection failed: {ex.Message}";
            ConnectionState = "disconnected";
            logger.LogWarning(ex, "War board hub startup failed for {Endpoint}", HubEndpoint);
        }
    }

    private HubConnection BuildHubConnection()
    {
        var baseAddress = http.BaseAddress ?? throw new InvalidOperationException("WarBoardService requires an HttpClient BaseAddress.");
        var hubUrl = new Uri(baseAddress, HubEndpoint);

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = GetAccessTokenAsync;
            })
            .WithAutomaticReconnect()
            .Build();

        connection.On<WarStateDto>(HubEventName, state =>
        {
            ApplyState(state, "hub-delta");
        });

        connection.Reconnecting += error =>
        {
            ConnectionError = error?.Message;
            ConnectionState = "reconnecting";
            NotifyStateChanged();
            return Task.CompletedTask;
        };

        connection.Reconnected += _ =>
        {
            ConnectionError = null;
            ConnectionState = "connected";
            NotifyStateChanged();
            return Task.CompletedTask;
        };

        connection.Closed += error =>
        {
            ConnectionError = error?.Message;
            ConnectionState = "disconnected";
            NotifyStateChanged();
            return Task.CompletedTask;
        };

        return connection;
    }

    private void ApplyState(WarStateDto state, string source)
    {
        CurrentState = state;
        CurrentFailure = null;
        DataSource = source;
        if (source == "hub-delta")
        {
            LastHubMessageAtUtc = DateTimeOffset.UtcNow;
        }

        NotifyStateChanged();
    }

    private Task<string?> GetAccessTokenAsync() => accessTokenProvider.GetAccessTokenAsync();

    private async Task ApplyAccessTokenAsync()
    {
        var accessToken = await GetAccessTokenAsync();
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
    }

    private static bool ContainsOperationalWarning(IReadOnlyList<string>? warnings)
    {
        if (warnings is null || warnings.Count == 0)
        {
            return false;
        }

        foreach (var warning in warnings)
        {
            if (warning.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
                warning.Contains("not ready", StringComparison.OrdinalIgnoreCase) ||
                warning.Contains("degraded", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void NotifyStateChanged() => StateChanged?.Invoke();
}

internal sealed class AccessTokenForwardingHandler(IServerAccessTokenProvider accessTokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var accessToken = await accessTokenProvider.GetAccessTokenAsync();
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
