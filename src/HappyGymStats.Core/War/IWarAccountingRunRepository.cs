using System.Security.Cryptography;
using System.Text;

namespace HappyGymStats.Core.War;

public sealed record WarAccountingSourceMemberFact(
    long FactionId,
    long WarId,
    long MemberId,
    string MemberName,
    int Score,
    int Chain,
    int Attacks,
    DateTimeOffset CapturedAtUtc);

/// <summary>
/// Immutable aggregate source facts captured at the accounting freeze boundary.
/// The fingerprint is a canonical SHA-256 over the exact scoped member facts.
/// </summary>
public sealed record FrozenWarAccountingSource(
    Guid SourceSnapshotId,
    long FactionId,
    long WarId,
    string Fingerprint,
    string CapturedBy,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<WarAccountingSourceMemberFact> Members);

public static class WarAccountingSourceFingerprint
{
    private const string FormatMarker = "hgs-war-accounting-source-v1";

    public static FrozenWarAccountingSource Create(
        Guid sourceSnapshotId,
        long factionId,
        long warId,
        IEnumerable<WarAccountingSourceMemberFact> members,
        string capturedBy,
        DateTimeOffset capturedAtUtc)
    {
        if (sourceSnapshotId == Guid.Empty)
            throw new ArgumentException("Source snapshot id must be non-empty.", nameof(sourceSnapshotId));
        if (string.IsNullOrWhiteSpace(capturedBy))
            throw new ArgumentException("Captured-by identity must be non-empty.", nameof(capturedBy));

        var normalizedActor = capturedBy.Trim();
        if (normalizedActor.Length > 200)
            throw new ArgumentOutOfRangeException(nameof(capturedBy), "Captured-by identity cannot exceed 200 characters.");

        var canonical = Canonicalize(factionId, warId, members);
        return new FrozenWarAccountingSource(
            sourceSnapshotId,
            factionId,
            warId,
            ComputeCanonical(factionId, warId, canonical),
            normalizedActor,
            capturedAtUtc.ToUniversalTime(),
            Array.AsReadOnly(canonical));
    }

    public static string Compute(
        long factionId,
        long warId,
        IEnumerable<WarAccountingSourceMemberFact> members)
    {
        var canonical = Canonicalize(factionId, warId, members);
        return ComputeCanonical(factionId, warId, canonical);
    }

    private static WarAccountingSourceMemberFact[] Canonicalize(
        long factionId,
        long warId,
        IEnumerable<WarAccountingSourceMemberFact> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (factionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(factionId), factionId, "Faction id must be positive.");
        if (warId <= 0)
            throw new ArgumentOutOfRangeException(nameof(warId), warId, "War id must be positive.");

        var seenMembers = new HashSet<long>();
        var canonical = members
            .Select(member => NormalizeMember(factionId, warId, member, seenMembers))
            .OrderBy(member => member.MemberId)
            .ToArray();

        return canonical;
    }

    private static WarAccountingSourceMemberFact NormalizeMember(
        long factionId,
        long warId,
        WarAccountingSourceMemberFact member,
        ISet<long> seenMembers)
    {
        if (member.FactionId != factionId || member.WarId != warId)
            throw new ArgumentException("Every source member fact must match the requested faction and war scope.", nameof(member));
        if (member.MemberId <= 0)
            throw new ArgumentOutOfRangeException(nameof(member), member.MemberId, "Member id must be positive.");
        if (!seenMembers.Add(member.MemberId))
            throw new ArgumentException($"Duplicate source member id {member.MemberId}.", nameof(member));
        if (string.IsNullOrWhiteSpace(member.MemberName))
            throw new ArgumentException("Source member name must be non-empty.", nameof(member));

        var memberName = member.MemberName.Trim();
        if (memberName.Length > 128)
            throw new ArgumentOutOfRangeException(nameof(member), "Source member name cannot exceed 128 characters.");
        if (member.Score < 0)
            throw new ArgumentOutOfRangeException(nameof(member), member.Score, "Source score cannot be negative.");
        if (member.Chain < 0)
            throw new ArgumentOutOfRangeException(nameof(member), member.Chain, "Source chain cannot be negative.");
        if (member.Attacks < 0)
            throw new ArgumentOutOfRangeException(nameof(member), member.Attacks, "Source attacks cannot be negative.");

        return member with
        {
            MemberName = memberName,
            CapturedAtUtc = member.CapturedAtUtc.ToUniversalTime()
        };
    }

    private static string ComputeCanonical(
        long factionId,
        long warId,
        IReadOnlyList<WarAccountingSourceMemberFact> members)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(FormatMarker);
            writer.Write(factionId);
            writer.Write(warId);
            writer.Write(members.Count);

            foreach (var member in members)
            {
                writer.Write(member.MemberId);
                writer.Write(member.MemberName);
                writer.Write(member.Score);
                writer.Write(member.Chain);
                writer.Write(member.Attacks);
                writer.Write(member.CapturedAtUtc.UtcDateTime.Ticks);
            }
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }
}

/// <summary>
/// Minimal immutable audit binding for a frozen accounting/payout run.
/// ObjectiveVersion and SourceSnapshotId identify the exact durable inputs used by the run;
/// richer policy/line persistence is layered onto this boundary by #88.
/// </summary>
public sealed record FrozenWarAccountingRun(
    Guid RunId,
    long FactionId,
    long WarId,
    int ObjectiveVersion,
    Guid SourceSnapshotId,
    string FrozenBy,
    DateTimeOffset FrozenAtUtc);

public enum WarAccountingRunLifecycleKind
{
    Approved = 1,
    Superseded = 2
}

/// <summary>
/// Append-only lifecycle evidence for a frozen run. Approval and supersession are
/// events rather than mutable flags so actor, time and reason remain auditable.
/// </summary>
public sealed record WarAccountingRunLifecycleEvent(
    Guid EventId,
    Guid RunId,
    WarAccountingRunLifecycleKind Kind,
    string Actor,
    DateTimeOffset OccurredAtUtc,
    string Reason,
    Guid? SupersedingRunId);

public interface IWarAccountingRunRepository
{
    /// <summary>
    /// Freezes the currently effective objective and the exact currently persisted
    /// aggregate ranked-war member facts into one immutable accounting run.
    /// If no objective has been configured yet, the canonical baseline version 1 is
    /// materialized first. Objective selection is serialized with objective appends.
    /// </summary>
    Task<FrozenWarAccountingRun> FreezeAsync(
        Guid runId,
        long factionId,
        long warId,
        string frozenBy,
        DateTimeOffset frozenAtUtc,
        CancellationToken ct);

    Task<FrozenWarAccountingRun?> GetAsync(Guid runId, CancellationToken ct);

    Task<FrozenWarAccountingSource?> GetSourceAsync(Guid sourceSnapshotId, CancellationToken ct);

    /// <summary>
    /// Appends the one approval event for a frozen run. Re-approval and approval after
    /// supersession are rejected by the persistence contract.
    /// </summary>
    Task<WarAccountingRunLifecycleEvent> ApproveAsync(
        Guid eventId,
        Guid runId,
        string actor,
        DateTimeOffset occurredAtUtc,
        string reason,
        CancellationToken ct);

    /// <summary>
    /// Supersedes an approved run with another approved frozen run from the same
    /// faction/war scope. The replacement relationship is immutable and auditable.
    /// </summary>
    Task<WarAccountingRunLifecycleEvent> SupersedeAsync(
        Guid eventId,
        Guid runId,
        Guid supersedingRunId,
        string actor,
        DateTimeOffset occurredAtUtc,
        string reason,
        CancellationToken ct);

    Task<IReadOnlyList<WarAccountingRunLifecycleEvent>> GetLifecycleAsync(
        Guid runId,
        CancellationToken ct);
}
