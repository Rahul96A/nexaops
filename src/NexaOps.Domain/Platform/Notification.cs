using NexaOps.Domain.Common;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Domain.Platform;

/// <summary>A message delivered to one user, in the product and optionally by email.</summary>
public class Notification : TenantEntity
{
    public Guid RecipientUserId { get; set; }

    public NotificationKind Kind { get; set; }
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Information;

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public ServiceModule? Module { get; set; }
    public Guid? RecordId { get; set; }

    /// <summary>Relative in-app route, e.g. /incidents/{id}. Never an absolute external URL.</summary>
    public string? ActionUrl { get; set; }

    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAt { get; set; }

    // --- Email delivery state, tracked so failures are visible rather than silent. ---
    public bool EmailRequested { get; set; }
    public DateTimeOffset? EmailSentAt { get; set; }
    public string? EmailFailureReason { get; set; }
    public int EmailAttempts { get; set; }
}

public enum NotificationKind
{
    IncidentAssigned = 1,
    IncidentReassigned = 2,
    IncidentCommented = 3,
    IncidentResolved = 4,
    IncidentClosed = 5,
    IncidentPriorityChanged = 6,
    IncidentReopened = 7,
    SlaWarning = 8,
    SlaBreached = 9,
    ApprovalRequested = 10,
    ApprovalDecided = 11,
    ChangeScheduled = 12,
    Mention = 13,
    SystemAnnouncement = 14,

    // Service request management.
    RequestSubmitted = 15,
    RequestAssigned = 16,
    RequestCommented = 17,
    RequestFulfilled = 18,
    RequestRejected = 19,
    RequestCancelled = 20,

    /// <summary>Raised by an automation rule rather than by a person's action.</summary>
    WorkflowNotification = 21
}

public enum NotificationSeverity
{
    Information = 1,
    Success = 2,
    Warning = 3,
    Critical = 4
}
