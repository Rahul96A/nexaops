using NexaOps.Application.Common;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Sla;

namespace NexaOps.Application.Incidents;

/// <summary>A row in an incident list view. Deliberately flat and small - list pages fetch many.</summary>
public sealed record IncidentListItemDto(
    Guid Id,
    string Number,
    string Title,
    IncidentStatus Status,
    Priority Priority,
    Impact Impact,
    Urgency Urgency,
    Guid? CategoryId,
    string? CategoryName,
    string? SubcategoryName,
    Guid RequesterId,
    string RequesterName,
    Guid? AssignedToUserId,
    string? AssignedToName,
    Guid? AssignmentGroupId,
    string? AssignmentGroupName,
    IncidentChannel Channel,
    bool IsMajorIncident,
    bool HasBreachedSla,
    DateTimeOffset? NextSlaDueAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? ResolvedAt);

/// <summary>The full incident record shown on the detail page.</summary>
public sealed record IncidentDetailDto
{
    public required Guid Id { get; init; }
    public required string Number { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }

    public required IncidentStatus Status { get; init; }
    public PendingReason? PendingReason { get; init; }
    public required Impact Impact { get; init; }
    public required Urgency Urgency { get; init; }
    public required Priority Priority { get; init; }
    public bool IsPriorityOverridden { get; init; }
    public string? PriorityOverrideReason { get; init; }
    public required IncidentChannel Channel { get; init; }
    public bool IsMajorIncident { get; init; }

    public required Guid RequesterId { get; init; }
    public required string RequesterName { get; init; }
    public string? RequesterEmail { get; init; }
    public Guid? AffectedUserId { get; init; }
    public string? AffectedUserName { get; init; }

    public Guid? OrganizationId { get; init; }
    public string? OrganizationName { get; init; }
    public Guid? DepartmentId { get; init; }
    public string? DepartmentName { get; init; }

    public Guid? CategoryId { get; init; }
    public string? CategoryName { get; init; }
    public Guid? SubcategoryId { get; init; }
    public string? SubcategoryName { get; init; }

    public Guid? AssignmentGroupId { get; init; }
    public string? AssignmentGroupName { get; init; }
    public Guid? AssignedToUserId { get; init; }
    public string? AssignedToName { get; init; }

    public ResolutionCode? ResolutionCode { get; init; }
    public string? ResolutionNotes { get; init; }

    public DateTimeOffset? FirstRespondedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
    public string? ResolvedByName { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public int ReopenCount { get; init; }

    public Guid? ParentIncidentId { get; init; }
    public string? ParentIncidentNumber { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public string? CreatedByName { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? UpdatedByName { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }
    public required IReadOnlyList<SlaInstanceDto> SlaInstances { get; init; }

    /// <summary>Statuses this incident may legally move to, so the UI need not duplicate the rules.</summary>
    public required IReadOnlyList<IncidentStatus> AllowedTransitions { get; init; }

    public int AttachmentCount { get; init; }
    public int CommentCount { get; init; }

    /// <summary>Base64 row version for optimistic concurrency on the next update.</summary>
    public string? ConcurrencyToken { get; init; }
}

/// <summary>A live SLA clock as rendered in the UI.</summary>
public sealed record SlaInstanceDto(
    Guid Id,
    string Name,
    SlaTargetType TargetType,
    SlaState State,
    DateTimeOffset StartedAt,
    DateTimeOffset DueAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? BreachedAt,
    int DurationMinutes,
    int ElapsedMinutes,
    int RemainingMinutes,
    int ConsumedPercent,
    int WarningThresholdPercent);

/// <summary>A comment or work note on an incident.</summary>
public sealed record IncidentCommentDto(
    Guid Id,
    IncidentCommentKind Kind,
    string Body,
    Guid AuthorUserId,
    string AuthorName,
    string? AuthorAvatarColor,
    bool IsSystemGenerated,
    DateTimeOffset CreatedAt);

/// <summary>One entry in the merged activity timeline: comments plus audited field changes.</summary>
public sealed record IncidentActivityDto(
    Guid Id,
    string Type,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    string? ActorName,
    string? ActorAvatarColor,
    string? Body,
    IncidentCommentKind? CommentKind,
    bool IsSystemGenerated,
    IReadOnlyList<IncidentFieldChangeDto>? Changes);

/// <summary>A single field change rendered in the timeline, in display terms rather than raw values.</summary>
public sealed record IncidentFieldChangeDto(string Field, string? From, string? To);

/// <summary>Named saved view over the incident queue.</summary>
public enum IncidentViewScope
{
    /// <summary>Everything the caller is allowed to see.</summary>
    All = 0,

    /// <summary>Assigned to the caller.</summary>
    AssignedToMe = 1,

    /// <summary>Assigned to a group the caller belongs to.</summary>
    MyTeam = 2,

    /// <summary>Raised by, or affecting, the caller.</summary>
    RaisedByMe = 3,

    /// <summary>In one of the caller's groups but with no individual owner.</summary>
    Unassigned = 4,

    /// <summary>Open and already past an SLA deadline.</summary>
    Breached = 5,

    /// <summary>Open, running, and inside the warning threshold of an SLA deadline.</summary>
    DueSoon = 6
}

/// <summary>Filter, sort and page parameters for the incident list.</summary>
public sealed class IncidentQuery : PagedQuery
{
    /// <summary>Free text over number, title and description.</summary>
    public string? Search { get; set; }

    public IncidentViewScope Scope { get; set; } = IncidentViewScope.All;

    public IReadOnlyList<IncidentStatus>? Statuses { get; set; }
    public IReadOnlyList<Priority>? Priorities { get; set; }

    /// <summary>Convenience filter for "everything still being worked".</summary>
    public bool? OpenOnly { get; set; }

    public Guid? AssignedToUserId { get; set; }
    public Guid? AssignmentGroupId { get; set; }
    public Guid? RequesterId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? SubcategoryId { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? DepartmentId { get; set; }

    public bool? IsMajorIncident { get; set; }
    public bool? HasBreachedSla { get; set; }

    public DateTimeOffset? CreatedFrom { get; set; }
    public DateTimeOffset? CreatedTo { get; set; }

    public string? Tag { get; set; }

    /// <summary>Include archived records. Requires <c>incident.archive</c>.</summary>
    public bool IncludeArchived { get; set; }

    /// <summary>Columns a client may sort by. Anything else is rejected rather than interpolated.</summary>
    public static readonly IReadOnlySet<string> SortableFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "number", "title", "status", "priority", "impact", "urgency",
        "createdAt", "updatedAt", "resolvedAt", "nextSlaDueAt", "requesterName", "assignedToName"
    };
}
