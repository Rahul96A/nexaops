using NexaOps.Domain.Common;
using NexaOps.Domain.Identity;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Domain.Requests;

/// <summary>
/// Correspondence on a service request.
/// <para>
/// Reuses <see cref="IncidentCommentKind"/> rather than declaring a parallel enum, because the
/// distinction is identical and having two enums that mean the same thing is how work notes end
/// up leaking in one module and not the other.
/// </para>
/// </summary>
public class RequestComment : TenantEntity
{
    public Guid ServiceRequestId { get; set; }

    public IncidentCommentKind Kind { get; set; } = IncidentCommentKind.PublicComment;

    public string Body { get; set; } = string.Empty;

    public Guid AuthorId { get; set; }

    /// <summary>Author name captured at write time, so the trail reads correctly after a rename.</summary>
    public string AuthorDisplayName { get; set; } = string.Empty;

    public ServiceRequest? ServiceRequest { get; set; }
    public User? Author { get; set; }
}
