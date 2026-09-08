using NexaOps.Application.Common;

namespace NexaOps.Application.Incidents;

/// <summary>
/// The incident use cases. Every method enforces permissions, applies domain rules, keeps SLA
/// clocks in step, raises notifications, and leaves an audit trail.
/// <para>
/// This is the only way incidents change. The REST controllers, the workflow engine and
/// confirmed AI actions all come through here, which is what makes "the AI cannot bypass
/// authorization" a structural property rather than a promise.
/// </para>
/// </summary>
public interface IIncidentService
{
    Task<PagedResult<IncidentListItemDto>> SearchAsync(
        IncidentQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Full detail. Throws when the incident does not exist or is not visible.</summary>
    Task<IncidentDetailDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IncidentDetailDto> GetByNumberAsync(string number, CancellationToken cancellationToken = default);

    Task<IncidentDetailDto> CreateAsync(
        CreateIncidentCommand command,
        CancellationToken cancellationToken = default);

    Task<IncidentDetailDto> UpdateAsync(
        Guid id,
        UpdateIncidentCommand command,
        CancellationToken cancellationToken = default);

    Task<IncidentDetailDto> AssignAsync(
        Guid id,
        AssignIncidentCommand command,
        CancellationToken cancellationToken = default);

    Task<IncidentDetailDto> ChangeStatusAsync(
        Guid id,
        ChangeIncidentStatusCommand command,
        CancellationToken cancellationToken = default);

    Task<IncidentDetailDto> ChangePriorityAsync(
        Guid id,
        ChangeIncidentPriorityCommand command,
        CancellationToken cancellationToken = default);

    Task<IncidentDetailDto> DeclareMajorAsync(
        Guid id,
        DeclareMajorIncidentCommand command,
        CancellationToken cancellationToken = default);

    Task<IncidentCommentDto> AddCommentAsync(
        Guid id,
        AddIncidentCommentCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IncidentCommentDto>> GetCommentsAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IncidentActivityDto>> GetActivityAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<ServiceDeskSummaryDto> GetServiceDeskSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>Archives an incident. Records are never hard-deleted.</summary>
    Task ArchiveAsync(Guid id, string reason, CancellationToken cancellationToken = default);
}
