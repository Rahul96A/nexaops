using NexaOps.Domain.Auditing;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Application.Abstractions;

/// <summary>
/// Records audit events that are not simple entity mutations - authentication outcomes,
/// permission denials, AI tool executions, exports.
/// <para>
/// Entity create/update/archive is captured automatically by the persistence interceptor, so
/// application services do not need to call this for ordinary edits. Use it when the meaning
/// of an action is not visible in the column diff.
/// </para>
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Queues an audit event to be written with the current unit of work, so the audit row and
    /// the change it describes commit together.
    /// </summary>
    void Record(
        AuditAction action,
        string entityType,
        string? entityId = null,
        string? entityLabel = null,
        string? message = null,
        AuditSource source = AuditSource.Api,
        AuditOutcome outcome = AuditOutcome.Success,
        object? before = null,
        object? after = null);

    /// <summary>
    /// Writes an audit event immediately in its own transaction. Required for events that must
    /// survive a failed or rolled-back operation - a denied permission, a failed sign-in, a
    /// tenant isolation violation.
    /// </summary>
    Task RecordImmediateAsync(
        AuditAction action,
        string entityType,
        string? entityId = null,
        string? entityLabel = null,
        string? message = null,
        AuditSource source = AuditSource.Api,
        AuditOutcome outcome = AuditOutcome.Success,
        Guid? tenantIdOverride = null,
        Guid? actorUserIdOverride = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Read access to the audit trail. Callers must hold <c>audit.read</c>.</summary>
public interface IAuditQueryService
{
    Task<Common.PagedResult<AuditEventDto>> SearchAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Full history for one record, newest first. Powers the record audit tab.</summary>
    Task<IReadOnlyList<AuditEventDto>> GetForRecordAsync(
        string entityType,
        Guid entityId,
        int limit = 200,
        CancellationToken cancellationToken = default);
}

/// <summary>Filter for an audit trail search.</summary>
public sealed class AuditQuery : Common.PagedQuery
{
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public Guid? ActorUserId { get; set; }
    public AuditAction? Action { get; set; }
    public AuditSource? Source { get; set; }
    public AuditOutcome? Outcome { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public string? CorrelationId { get; set; }

    /// <summary>Free-text match over the entity label and message.</summary>
    public string? Search { get; set; }
}

/// <summary>An audit row as presented to an administrator.</summary>
public sealed record AuditEventDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    string? ActorDisplayName,
    string Action,
    string EntityType,
    string? EntityId,
    string? EntityLabel,
    string Source,
    string Outcome,
    string? ChangedFields,
    string? BeforeJson,
    string? AfterJson,
    string? Message,
    string? CorrelationId,
    string? IpAddress);
