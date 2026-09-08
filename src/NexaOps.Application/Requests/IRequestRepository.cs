using NexaOps.Application.Common;
using NexaOps.Domain.Approvals;
using NexaOps.Domain.Catalog;
using NexaOps.Domain.Requests;

namespace NexaOps.Application.Requests;

/// <summary>
/// Write-side access to service request aggregates.
/// <para>
/// Everything returned is a tracked domain entity, already filtered to the ambient tenant by the
/// persistence layer. The application service mutates it through domain methods and commits once.
/// </para>
/// </summary>
public interface IRequestRepository
{
    Task<ServiceRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Loads a request with its lines and approvals, for a decision or a fulfilment.</summary>
    Task<ServiceRequest?> GetWithLinesAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ServiceRequest?> GetByNumberAsync(string number, CancellationToken cancellationToken = default);

    void Add(ServiceRequest request);

    void AddItem(RequestItem item);

    void AddComment(RequestComment comment);

    void AddApproval(Approval approval);

    /// <summary>
    /// Declares the row version the caller read, so a concurrent change is detected.
    /// <para>
    /// Lives in the port for the same reason it does on the incident side: assigning the
    /// property on the entity does nothing, because optimistic concurrency compares the version
    /// the change tracker loaded rather than the value sitting on the object.
    /// </para>
    /// </summary>
    void SetExpectedVersion(ServiceRequest request, byte[] rowVersion);

    /// <summary>Refreshes the denormalised SLA roll-up columns after any clock change.</summary>
    Task RefreshSlaRollUpAsync(Guid requestId, CancellationToken cancellationToken = default);
}

/// <summary>Write-side access to the service catalogue.</summary>
public interface ICatalogRepository
{
    Task<CatalogItem?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Loads an item with its variable definitions, for editing or for ordering.</summary>
    Task<CatalogItem?> GetWithVariablesAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Loads several items with their variables in one round trip, for a basket.</summary>
    Task<IReadOnlyList<CatalogItem>> GetManyWithVariablesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, Guid? exceptId, CancellationToken cancellationToken = default);

    void Add(CatalogItem item);

    void AddVariable(CatalogItemVariable variable);

    void RemoveVariables(IEnumerable<CatalogItemVariable> variables);

    void SetExpectedVersion(CatalogItem item, byte[] rowVersion);
}

/// <summary>Write-side access to approvals, across every module that uses them.</summary>
public interface IApprovalRepository
{
    Task<Approval?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every approval on one record, ordered by stage.</summary>
    Task<IReadOnlyList<Approval>> GetForRecordAsync(
        string module,
        Guid recordId,
        CancellationToken cancellationToken = default);

    void Add(Approval approval);
}

/// <summary>Read-side queries for requests, the catalogue and approvals.</summary>
public interface IRequestQueryService
{
    Task<PagedResult<RequestSummaryDto>> SearchAsync(
        RequestQuery query,
        CancellationToken cancellationToken = default);

    Task<RequestDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<RequestDetailDto?> GetByNumberAsync(string number, CancellationToken cancellationToken = default);

    /// <summary>
    /// Correspondence on a request, or null when the caller cannot see the request at all -
    /// which the service turns into a 404, matching how every other cross-tenant or invisible
    /// record is reported.
    /// </summary>
    Task<IReadOnlyList<RequestCommentDto>?> GetCommentsAsync(
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task<RequestSummaryCountsDto> GetSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>Approvals addressed to the caller, personally or through one of their groups.</summary>
    Task<IReadOnlyList<ApprovalDto>> GetMyApprovalsAsync(
        bool outstandingOnly,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogItemSummaryDto>> BrowseCatalogAsync(
        string? search,
        Guid? categoryId,
        bool includeUnpublished,
        CancellationToken cancellationToken = default);

    Task<CatalogItemDetailDto?> GetCatalogItemAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
