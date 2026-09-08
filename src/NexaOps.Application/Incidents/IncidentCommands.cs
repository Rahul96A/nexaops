using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Application.Incidents;

/// <summary>Input for raising an incident.</summary>
public sealed class CreateIncidentCommand
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Who is reporting. Optional: when omitted the caller is used. Supplying someone else
    /// requires <c>incident.update</c>, so an ordinary employee cannot raise tickets as a
    /// colleague.
    /// </summary>
    public Guid? RequesterId { get; set; }

    public Guid? AffectedUserId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? SubcategoryId { get; set; }

    public Impact Impact { get; set; } = Impact.Moderate;
    public Urgency Urgency { get; set; } = Urgency.Medium;

    public Guid? AssignmentGroupId { get; set; }
    public Guid? AssignedToUserId { get; set; }

    public IncidentChannel Channel { get; set; } = IncidentChannel.Portal;

    public IReadOnlyList<string>? Tags { get; set; }

    /// <summary>Set when this incident is a child of a declared major incident.</summary>
    public Guid? ParentIncidentId { get; set; }
}

/// <summary>
/// Input for editing an incident. Null means "leave unchanged", which is why every field is
/// nullable - a PATCH that omits a field must not blank it.
/// </summary>
public sealed class UpdateIncidentCommand
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? SubcategoryId { get; set; }
    public Impact? Impact { get; set; }
    public Urgency? Urgency { get; set; }
    public Guid? AffectedUserId { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? DepartmentId { get; set; }
    public IReadOnlyList<string>? Tags { get; set; }

    /// <summary>Base64 row version from the last read. Rejects a blind overwrite with 409.</summary>
    public string? ConcurrencyToken { get; set; }
}

/// <summary>Sets ownership. Both fields may be null, which returns the incident to the pool.</summary>
public sealed class AssignIncidentCommand
{
    public Guid? AssignmentGroupId { get; set; }
    public Guid? AssignedToUserId { get; set; }

    /// <summary>Optional note recorded as a work note alongside the assignment.</summary>
    public string? Note { get; set; }
}

/// <summary>Moves the incident through its lifecycle.</summary>
public sealed class ChangeIncidentStatusCommand
{
    public IncidentStatus Status { get; set; }

    /// <summary>Required when moving to Pending, so the SLA pause is explainable.</summary>
    public PendingReason? PendingReason { get; set; }

    /// <summary>Required when moving to Resolved.</summary>
    public ResolutionCode? ResolutionCode { get; set; }

    /// <summary>Required when moving to Resolved, and when reopening or cancelling.</summary>
    public string? Notes { get; set; }
}

/// <summary>Re-derives priority from impact and urgency, or overrides it explicitly.</summary>
public sealed class ChangeIncidentPriorityCommand
{
    public Impact? Impact { get; set; }
    public Urgency? Urgency { get; set; }

    /// <summary>
    /// Explicit priority. Requires <c>incident.priority.override</c> and a reason; without it
    /// priority is always derived from the tenant impact/urgency matrix.
    /// </summary>
    public Priority? OverridePriority { get; set; }

    public string? OverrideReason { get; set; }
}

/// <summary>Adds a comment or work note.</summary>
public sealed class AddIncidentCommentCommand
{
    public string Body { get; set; } = string.Empty;

    /// <summary>Work notes require <c>incident.worknote.create</c>.</summary>
    public IncidentCommentKind Kind { get; set; } = IncidentCommentKind.PublicComment;
}

/// <summary>Declares or withdraws major incident status.</summary>
public sealed class DeclareMajorIncidentCommand
{
    public bool IsMajorIncident { get; set; }

    /// <summary>Mandatory justification, recorded as a work note and audited.</summary>
    public string Reason { get; set; } = string.Empty;
}
