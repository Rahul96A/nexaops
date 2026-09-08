using NexaOps.Domain.Common;

namespace NexaOps.Domain.Auditing;

/// <summary>
/// An append-only record of something that happened. Audit rows are never updated or deleted
/// by any code path in the application; the only exposed operations are insert and read, and
/// reading requires the audit.read permission.
/// </summary>
public class AuditEvent : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    public Guid? ActorUserId { get; set; }

    /// <summary>Denormalised so the trail stays readable after a user is renamed or archived.</summary>
    public string? ActorDisplayName { get; set; }
    public string? ActorEmail { get; set; }

    public AuditAction Action { get; set; }

    /// <summary>CLR type name of the affected entity, e.g. Incident.</summary>
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }

    /// <summary>Human-readable label, e.g. the incident number, so the trail reads without joins.</summary>
    public string? EntityLabel { get; set; }

    /// <summary>JSON snapshot of changed properties before the change. Null for creates.</summary>
    public string? BeforeJson { get; set; }

    /// <summary>JSON snapshot of changed properties after the change. Null for deletes.</summary>
    public string? AfterJson { get; set; }

    /// <summary>Comma-separated list of changed property names, for cheap filtering.</summary>
    public string? ChangedFields { get; set; }

    public AuditSource Source { get; set; } = AuditSource.Api;
    public AuditOutcome Outcome { get; set; } = AuditOutcome.Success;

    /// <summary>Ties this event to a single HTTP request or background job across all logs.</summary>
    public string? CorrelationId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>Free-form context, e.g. the denied permission code or the AI tool name.</summary>
    public string? Message { get; set; }
}

/// <summary>What kind of operation the audit event records.</summary>
public enum AuditAction
{
    Create = 1,
    Update = 2,
    Archive = 3,
    Restore = 4,
    Read = 5,
    Assign = 6,
    StatusChange = 7,
    Approve = 8,
    Reject = 9,
    PermissionChange = 10,
    Login = 11,
    LoginFailed = 12,
    Logout = 13,
    AccessDenied = 14,
    Export = 15,
    WorkflowExecution = 16,
    AiToolExecution = 17,
    AiAgentAction = 18,
    AttachmentUpload = 19,
    AttachmentDownload = 20,
    Comment = 21,
    SecurityEvent = 22,
    Impersonation = 23,
    Configuration = 24
}

/// <summary>Which subsystem initiated the action. Distinguishes human from automated activity.</summary>
public enum AuditSource
{
    Api = 1,
    Ai = 2,
    Workflow = 3,
    System = 4,
    Integration = 5,
    Import = 6
}

public enum AuditOutcome
{
    Success = 1,
    Failure = 2,
    Denied = 3
}
