using NexaOps.Application.Common;
using NexaOps.Domain.Platform;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Sla;

namespace NexaOps.Application.Incidents;

/// <summary>
/// Write-side access to incident aggregates.
/// <para>
/// Everything returned here is a tracked domain entity, already filtered to the ambient tenant
/// by the persistence layer. The application service mutates it through domain methods and then
/// commits once via <see cref="Abstractions.IUnitOfWork"/>.
/// </para>
/// </summary>
public interface IIncidentRepository
{
    /// <summary>Loads an incident for modification, or null when it does not exist in this tenant.</summary>
    Task<Incident?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Loads an incident together with its SLA clocks and tags, for a full-state update.</summary>
    Task<Incident?> GetWithClocksAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Incident?> GetByNumberAsync(string number, CancellationToken cancellationToken = default);

    void Add(Incident incident);

    /// <summary>
    /// Declares the row version the caller read, so a concurrent change is detected.
    /// <para>
    /// This has to live in the persistence port because assigning the property on the entity is
    /// not enough: optimistic concurrency compares the version the tracker loaded, not the value
    /// currently sitting on the object. Setting the property alone silently does nothing, which
    /// is precisely the failure this method exists to prevent.
    /// </para>
    /// </summary>
    void SetExpectedVersion(Incident incident, byte[] rowVersion);

    void AddComment(IncidentComment comment);

    void AddTag(IncidentTag tag);

    void RemoveTags(IEnumerable<IncidentTag> tags);

    /// <summary>
    /// Running or paused SLA clocks whose deadline or warning threshold has passed, across all
    /// tenants. Used only by the background SLA monitor, which establishes each tenant scope
    /// explicitly before acting on the results.
    /// </summary>
    Task<IReadOnlyList<SlaInstance>> GetClocksNeedingAttentionAsync(
        DateTimeOffset asOf,
        int maxResults,
        CancellationToken cancellationToken = default);

    /// <summary>Denormalised SLA roll-up on the incident, refreshed after any clock changes.</summary>
    Task RefreshSlaRollUpAsync(Guid incidentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-side access to incidents. Returns DTOs projected in the database, so a list page never
/// materialises full entity graphs and never triggers lazy loads.
/// </summary>
public interface IIncidentQueryService
{
    /// <summary>
    /// Searches incidents. The implementation applies the caller's visibility scope: a user
    /// without <c>incident.read.all</c> sees only incidents they raised, are affected by, are
    /// assigned, or that sit in one of their groups.
    /// </summary>
    Task<PagedResult<IncidentListItemDto>> SearchAsync(
        IncidentQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Full detail, or null when the incident does not exist or is not visible to the caller.</summary>
    Task<IncidentDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Comments newest-last. Work notes are excluded at the query level for callers without
    /// <c>incident.worknote.read</c>, so an internal note is never serialised to a browser that
    /// must not see it.
    /// </summary>
    Task<IReadOnlyList<IncidentCommentDto>> GetCommentsAsync(
        Guid incidentId,
        CancellationToken cancellationToken = default);

    /// <summary>Merged timeline of comments and audited field changes, newest first.</summary>
    Task<IReadOnlyList<IncidentActivityDto>> GetActivityAsync(
        Guid incidentId,
        int limit = 200,
        CancellationToken cancellationToken = default);

    /// <summary>Aggregated counters for the service desk dashboard, in one round trip.</summary>
    Task<ServiceDeskSummaryDto> GetServiceDeskSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the caller is permitted to see this incident.</summary>
    Task<bool> CanViewAsync(Guid incidentId, CancellationToken cancellationToken = default);
}

/// <summary>Counters shown on the service desk dashboard. Every number is a live query.</summary>
public sealed record ServiceDeskSummaryDto(
    int OpenIncidents,
    int CriticalOpen,
    int HighOpen,
    int UnassignedInMyGroups,
    int AssignedToMe,
    int RaisedByMe,
    int BreachedOpen,
    int DueWithinTwoHours,
    int ResolvedToday,
    int CreatedToday,
    IReadOnlyList<AgentWorkloadDto> TeamWorkload,
    IReadOnlyList<PriorityCountDto> OpenByPriority,
    IReadOnlyList<StatusCountDto> OpenByStatus);

/// <param name="UserId">Agent.</param>
/// <param name="DisplayName">Agent name.</param>
/// <param name="AvatarColor">Deterministic avatar tint.</param>
/// <param name="OpenCount">Open incidents assigned to them.</param>
/// <param name="BreachedCount">How many of those have breached an SLA.</param>
public sealed record AgentWorkloadDto(
    Guid UserId,
    string DisplayName,
    string? AvatarColor,
    int OpenCount,
    int BreachedCount);

public sealed record PriorityCountDto(Priority Priority, int Count);

public sealed record StatusCountDto(IncidentStatus Status, int Count);

/// <summary>Allocates gapless, collision-free human-facing record numbers.</summary>
public interface INumberSequenceService
{
    /// <summary>
    /// Reserves the next number for a sequence key, e.g. <c>INC</c>. Must be called inside the
    /// caller's transaction: the row is locked for update so two concurrent requests cannot be
    /// handed the same number.
    /// </summary>
    Task<string> NextAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>Look-ups the incident service needs, kept separate from the incident repository.</summary>
public interface IServiceDeskReferenceRepository
{
    Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> GroupExistsAsync(Guid groupId, CancellationToken cancellationToken = default);

    Task<bool> IsMemberOfGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken = default);

    Task<Category?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);

    Task<Subcategory?> GetSubcategoryAsync(Guid subcategoryId, CancellationToken cancellationToken = default);

    /// <summary>The tenant priority matrix, cached because it changes rarely and is read constantly.</summary>
    Task<IReadOnlyList<PriorityMatrixEntry>> GetPriorityMatrixAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The tenant's change advisory board, or null when none is configured.
    /// <para>
    /// Resolved by well-known group code rather than by a setting, so a tenant that has not set
    /// one up simply has no board and the change service says so, rather than reading a
    /// dangling identifier out of configuration.
    /// </para>
    /// </summary>
    Task<Guid?> GetChangeAdvisoryBoardIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The user's line manager, or null when none is recorded. Used to resolve manager approval
    /// at the moment a request is submitted rather than storing a rule that could go stale.
    /// </summary>
    Task<Guid?> GetManagerIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Organization and department of a user, used to stamp an incident at creation.</summary>
    Task<(Guid? OrganizationId, Guid? DepartmentId)> GetUserPlacementAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    void AddNotification(Notification notification);
}
