using System.Text;
using System.Text.Json;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.Torn;
using HappyGymStats.Core.Torn.Models;
using HappyGymStats.Data.Entities;
using HappyGymStats.Encryption;

namespace HappyGymStats.Core.Fetch;

public sealed record PerkFetchResult(int LogTypesCompleted, long TotalLogsAppended);

public sealed class PerkLogFetcher
{
    private readonly TornApiClient _client;
    private readonly IAffiliationEventRepository _affiliationRepo;
    private readonly ILogTypeRepository _logTypeRepo;
    private readonly IUserLogEntryRepository _userLogRepo;
    private readonly IFactionIdMapRepository _factionIdMapRepo;
    private readonly IFactionMembershipRepository _factionMembershipRepo;
    private readonly IUnitOfWork _unitOfWork;

    public PerkLogFetcher(
        TornApiClient client,
        IAffiliationEventRepository affiliationRepo,
        ILogTypeRepository logTypeRepo,
        IUserLogEntryRepository userLogRepo,
        IFactionIdMapRepository factionIdMapRepo,
        IFactionMembershipRepository factionMembershipRepo,
        IUnitOfWork unitOfWork)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _affiliationRepo = affiliationRepo ?? throw new ArgumentNullException(nameof(affiliationRepo));
        _logTypeRepo = logTypeRepo ?? throw new ArgumentNullException(nameof(logTypeRepo));
        _userLogRepo = userLogRepo ?? throw new ArgumentNullException(nameof(userLogRepo));
        _factionIdMapRepo = factionIdMapRepo ?? throw new ArgumentNullException(nameof(factionIdMapRepo));
        _factionMembershipRepo = factionMembershipRepo ?? throw new ArgumentNullException(nameof(factionMembershipRepo));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<PerkFetchResult> RunAsync(
        string apiKey,
        Guid anonymousId,
        IReadOnlyList<PerkLogType> logTypes,
        FetchOptions options,
        CancellationToken ct,
        byte[]? publicKey = null,
        Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key must be provided.", nameof(apiKey));

        await _logTypeRepo.AddRangeIfMissingAsync(
            logTypes.Select(t => new LogTypeEntity { LogTypeId = t.Id, LogTypeTitle = t.Title }),
            ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var existingLogIds = await _userLogRepo.GetExistingLogIdsAsync(anonymousId, ct);
        var existingAffiliationIds = await _affiliationRepo.GetExistingSourceLogIdsAsync(anonymousId, ct);

        int typesCompleted = 0;
        long totalAppended = 0;

        foreach (var logType in logTypes)
        {
            ct.ThrowIfCancellationRequested();

            var nextUrl = new Uri($"https://api.torn.com/v2/user/log?log={logType.Id}&limit=100");
            log?.Invoke($"Log type {logType.Id} ({logType.Title}): starting fetch.");

            long appended = 0;
            bool throttleFirst = true;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (!throttleFirst && options.ThrottleDelay > TimeSpan.Zero)
                    await Task.Delay(options.ThrottleDelay, ct).ConfigureAwait(false);
                throttleFirst = false;

                var page = await FetchWithRetryAsync(apiKey, nextUrl, options, ct, log).ConfigureAwait(false);
                var newLogs = new List<UserLog>(page.Logs.Count);

                foreach (var entry in page.Logs)
                {
                    if (string.IsNullOrEmpty(entry.Id))
                        continue;
                    if (!existingLogIds.Add(entry.Id))
                        continue;
                    newLogs.Add(entry);
                }

                if (newLogs.Count > 0)
                {
                    await _userLogRepo.AddRangeAsync(newLogs.Select(e => MapUserLogEntry(e, anonymousId, logType.Id)).ToList(), ct);

                    if (logType.Scope is PerkLogTypes.ScopeFaction or PerkLogTypes.ScopeCompany)
                    {
                        foreach (var entry in newLogs)
                        {
                            if (!existingAffiliationIds.Add(entry.Id))
                                continue;
                            var affEvent = TryExtractAffiliationEvent(entry, anonymousId, logType, publicKey);
                            if (affEvent is null)
                                continue;

                            await _affiliationRepo.AddAsync(affEvent, ct);

                            var factionAnonymousId = await _factionIdMapRepo
                                .GetOrCreateFactionAnonymousIdAsync(affEvent.AffiliationId, affEvent.Scope, ct)
                                .ConfigureAwait(false);
                            await _factionMembershipRepo
                                .AddOrIgnoreAsync(factionAnonymousId, anonymousId, ct)
                                .ConfigureAwait(false);
                        }
                    }

                    await _unitOfWork.SaveChangesAsync(ct);
                    appended += newLogs.Count;
                    totalAppended += newLogs.Count;
                }

                if (page.Logs.Count == 0 || page.NextUrl is null)
                {
                    log?.Invoke($"Log type {logType.Id} ({logType.Title}): done. Appended {appended} new log(s).");
                    typesCompleted++;
                    break;
                }

                nextUrl = page.NextUrl;
                log?.Invoke($"Log type {logType.Id}: page fetched={page.Logs.Count} appended={newLogs.Count}");
            }
        }

        return new PerkFetchResult(typesCompleted, totalAppended);
    }

    private async Task<UserLogPage> FetchWithRetryAsync(
        string apiKey,
        Uri url,
        FetchOptions options,
        CancellationToken ct,
        Action<string>? log)
    {
        var attempt = 0;
        var backoff = options.InitialBackoffDelay;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await _client.GetUserLogPageAsync(apiKey, url, ct).ConfigureAwait(false);
            }
            catch (TornApiException ex) when (ex.IsRetryable)
            {
                attempt++;
                if (attempt > options.MaxRetryAttempts)
                    throw;

                var delay = backoff;
                backoff = TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, options.MaxBackoffDelay.TotalMilliseconds));
                log?.Invoke($"Transient error (attempt {attempt}/{options.MaxRetryAttempts}) - backing off {delay.TotalSeconds:0.0}s: {ex.Message}");
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }

    private static UserLogEntryEntity MapUserLogEntry(UserLog entry, Guid anonymousId, int logTypeId)
    {
        int? happyBeforeApi = null;
        int? maxHappyAfter = null;
        int? propertyId = null;

        if (entry.Raw.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("happy_before", out var hb) && hb.TryGetInt32(out var hbVal))
                happyBeforeApi = hbVal;

            // Property logs: 5900 (upgrade), 5905 (staff), 5910 (move)
            // data.happy = resulting max happy; data.property_id = which property
            // 5900/5905 only apply when user is living in that property (resolved in LogEventExtractor)
            if (logTypeId is 5900 or 5905 or 5910)
            {
                if (data.TryGetProperty("happy", out var h) && h.TryGetInt32(out var hVal))
                    maxHappyAfter = hVal;
                if (data.TryGetProperty("property_id", out var pid) && pid.TryGetInt32(out var pidVal))
                    propertyId = pidVal;
            }

            // 5963 Education complete: working_stats_received is a "int,int,int" string (manual/intel/endurance).
            // TODO: check for data.battle_stats_received once logs with battle stats are available.
        }

        return new UserLogEntryEntity
        {
            AnonymousId = anonymousId,
            LogEntryId = entry.Id,
            OccurredAtUtc = entry.Timestamp > 0
                ? DateTimeOffset.FromUnixTimeSeconds(entry.Timestamp)
                : DateTimeOffset.UnixEpoch,
            LogTypeId = logTypeId,
            HappyBeforeApi = happyBeforeApi,
            MaxHappyAfter = maxHappyAfter,
            PropertyId = propertyId,
        };
    }

    private static AffiliationEventEntity? TryExtractAffiliationEvent(UserLog entry, Guid anonymousId, PerkLogType logType, byte[]? publicKey)
    {
        try
        {
            var raw = entry.Raw;
            int affiliationId = 0;
            int? senderId = null;
            int? positionBefore = null;
            int? positionAfter = null;

            if (raw.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                var affiliationKey = logType.Scope == PerkLogTypes.ScopeFaction ? "faction" : "company";
                if (data.TryGetProperty(affiliationKey, out var affProp) && affProp.TryGetInt32(out var affVal))
                    affiliationId = affVal;

                if (data.TryGetProperty("sender", out var senderProp) && senderProp.TryGetInt32(out var senderVal))
                    senderId = senderVal;
                if (data.TryGetProperty("position_before", out var posBefore) && posBefore.TryGetInt32(out var posBeforeVal))
                    positionBefore = posBeforeVal;
                if (data.TryGetProperty("position_after", out var posAfter) && posAfter.TryGetInt32(out var posAfterVal))
                    positionAfter = posAfterVal;
            }

            if (affiliationId == 0)
                return null;

            var scope = logType.Scope == PerkLogTypes.ScopeFaction
                ? AffiliationScope.Faction
                : AffiliationScope.Company;

            byte[]? encryptedAffiliationId = null;
            if (publicKey is not null)
                encryptedAffiliationId = Ecies.Encrypt(publicKey, Encoding.UTF8.GetBytes(affiliationId.ToString()));

            return new AffiliationEventEntity
            {
                AnonymousId = anonymousId,
                SourceLogEntryId = entry.Id,
                LogTypeId = logType.Id,
                Scope = scope,
                AffiliationId = affiliationId,
                SenderId = senderId,
                PositionBefore = positionBefore,
                PositionAfter = positionAfter,
                EncryptedAffiliationId = encryptedAffiliationId,
            };
        }
        catch
        {
            return null;
        }
    }
}
