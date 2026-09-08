using NexaOps.Domain.Common;
using NexaOps.Domain.Identity;

namespace NexaOps.Domain.ServiceDesk;

/// <summary>
/// A note on an incident. Public comments are correspondence with the requester; work notes
/// are internal and are filtered out of any response for a caller without
/// incident.worknote.read - filtering happens in the query, not in the serialiser, so an
/// internal note never travels to a browser that must not see it.
/// </summary>
public class IncidentComment : TenantEntity
{
    public Guid IncidentId { get; set; }
    public IncidentCommentKind Kind { get; set; } = IncidentCommentKind.PublicComment;

    public string Body { get; set; } = string.Empty;

    public Guid AuthorUserId { get; set; }

    /// <summary>True when written by automation (workflow, SLA escalation, confirmed AI action).</summary>
    public bool IsSystemGenerated { get; set; }

    public Incident? Incident { get; set; }
    public User? Author { get; set; }
}

/// <summary>A free-form label on an incident, normalised into its own row for indexed search.</summary>
public class IncidentTag : TenantEntity
{
    public Guid IncidentId { get; set; }

    /// <summary>Stored lower-cased and trimmed so that tag matching is predictable.</summary>
    public string Tag { get; set; } = string.Empty;

    public Incident? Incident { get; set; }
}
