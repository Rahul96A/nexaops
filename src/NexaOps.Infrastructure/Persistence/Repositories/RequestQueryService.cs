using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Requests;
using NexaOps.Application.Security;
using NexaOps.Domain.Approvals;
using NexaOps.Domain.Catalog;
using NexaOps.Domain.Requests;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Infrastructure.Persistence.Repositories;

/// <inheritdoc />
public sealed class RequestQueryService : IRequestQueryService
{
    private const string ApprovalModule = "Request";

    /// <summary>Shared by the scopes and the summary, so a counter and its list cannot disagree.</summary>
    private static readonly RequestStatus[] OpenStatuses =
    [
        RequestStatus.Draft, RequestStatus.AwaitingApproval, RequestStatus.Approved,
        RequestStatus.InProgress, RequestStatus.Pending
    ];

    private readonly NexaOpsDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public RequestQueryService(
        NexaOpsDbContext context,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>
    /// Applied as a database predicate, not a filter over loaded rows, so counts, paging and
    /// aggregates are all correct and an invisible request is indistinguishable from one that
    /// does not exist.
    /// </summary>
    private IQueryable<ServiceRequest> VisibleRequests()
    {
        var query = _context.ServiceRequests.AsNoTracking();

        if (_currentUser.HasPermission(Permissions.RequestReadAll))
        {
            return query;
        }

        var userId = _currentUser.UserIdOrNull ?? Guid.Empty;
        var groupIds = _currentUser.GroupIds.ToList();

        return query.Where(r =>
            r.RequesterId == userId
            || r.RequestedForId == userId
            || r.AssignedToUserId == userId
            || (r.FulfilmentGroupId != null && groupIds.Contains(r.FulfilmentGroupId.Value)));
    }

    /// <inheritdoc />
    public async Task<PagedResult<RequestSummaryDto>> SearchAsync(
        RequestQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var source = ApplyFilters(VisibleRequests(), query);

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0)
        {
            return PagedResult<RequestSummaryDto>.Empty(query.Page, query.PageSize);
        }

        var items = await ApplySort(source, query)
            .Skip((Math.Max(query.Page, 1) - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(r => new RequestSummaryDto(
                r.Id,
                r.Number,
                r.Title,
                r.Status,
                r.Priority,
                r.RequesterId,
                r.Requester!.DisplayName,
                r.RequestedForId,
                r.RequestedFor!.DisplayName,
                r.AssignedToUserId,
                r.AssignedTo!.DisplayName,
                r.AssignedTo!.AvatarColor,
                r.FulfilmentGroupId,
                r.FulfilmentGroup!.Name,
                r.Items.Count,
                r.TotalCost,
                r.HasBreachedSla,
                r.NextSlaDueAt,
                r.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<RequestSummaryDto>(items, total, query.Page, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<RequestDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var request = await VisibleRequests()
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return request is null ? null : await ProjectAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RequestDetailDto?> GetByNumberAsync(string number, CancellationToken cancellationToken = default)
    {
        var request = await VisibleRequests()
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Number == number, cancellationToken)
            .ConfigureAwait(false);

        return request is null ? null : await ProjectAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RequestCommentDto>?> GetCommentsAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        var visible = await VisibleRequests()
            .AnyAsync(r => r.Id == requestId, cancellationToken)
            .ConfigureAwait(false);

        if (!visible)
        {
            return null;
        }

        var query = _context.RequestComments
            .AsNoTracking()
            .Where(c => c.ServiceRequestId == requestId);

        // Work notes are excluded at the query level, not hidden in the browser.
        if (!_currentUser.HasPermission(Permissions.RequestWorkNoteRead))
        {
            query = query.Where(c => c.Kind == IncidentCommentKind.PublicComment);
        }

        return await query
            .OrderBy(c => c.CreatedAt)
            .Select(c => new RequestCommentDto(
                c.Id, c.Kind, c.Body, c.AuthorId, c.AuthorDisplayName, c.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RequestSummaryCountsDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserIdOrNull ?? Guid.Empty;
        var groupIds = _currentUser.GroupIds.ToList();
        var visible = VisibleRequests();

        var open = visible.Where(r => OpenStatuses.Contains(r.Status));

        var todayStart = _clock.UtcNow.Date;

        var openCount = await open.CountAsync(cancellationToken).ConfigureAwait(false);

        var awaitingApproval = await visible
            .CountAsync(r => r.Status == RequestStatus.AwaitingApproval, cancellationToken)
            .ConfigureAwait(false);

        var awaitingMyApproval = await _context.Approvals
            .AsNoTracking()
            .CountAsync(
                a => a.State == ApprovalState.Pending
                     && (a.ApproverUserId == userId
                         || (a.ApproverGroupId != null && groupIds.Contains(a.ApproverGroupId.Value))),
                cancellationToken)
            .ConfigureAwait(false);

        var unassigned = await open
            .CountAsync(r => r.AssignedToUserId == null, cancellationToken)
            .ConfigureAwait(false);

        var assignedToMe = await open
            .CountAsync(r => r.AssignedToUserId == userId, cancellationToken)
            .ConfigureAwait(false);

        var raisedByMe = await open
            .CountAsync(r => r.RequesterId == userId, cancellationToken)
            .ConfigureAwait(false);

        var breached = await open
            .CountAsync(r => r.HasBreachedSla, cancellationToken)
            .ConfigureAwait(false);

        var fulfilledToday = await visible
            .CountAsync(r => r.FulfilledAt != null && r.FulfilledAt >= todayStart, cancellationToken)
            .ConfigureAwait(false);

        var createdToday = await visible
            .CountAsync(r => r.CreatedAt >= todayStart, cancellationToken)
            .ConfigureAwait(false);

        var byStatus = await open
            .GroupBy(r => r.Status)
            .Select(g => new RequestStatusCountDto(g.Key, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new RequestSummaryCountsDto(
            openCount, awaitingApproval, awaitingMyApproval, unassigned, assignedToMe,
            raisedByMe, breached, fulfilledToday, createdToday, byStatus);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalDto>> GetMyApprovalsAsync(
        bool outstandingOnly,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserIdOrNull ?? Guid.Empty;
        var groupIds = _currentUser.GroupIds.ToList();

        var query = _context.Approvals.AsNoTracking();

        // approval.read.all is a management view. Everyone else sees only what is addressed to
        // them, personally or through a group they belong to.
        if (!_currentUser.HasPermission(Permissions.ApprovalReadAll))
        {
            query = query.Where(a =>
                a.ApproverUserId == userId
                || (a.ApproverGroupId != null && groupIds.Contains(a.ApproverGroupId.Value)));
        }

        if (outstandingOnly)
        {
            query = query.Where(a => a.State == ApprovalState.Pending);
        }

        return await ProjectApprovals(query.OrderBy(a => a.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogItemSummaryDto>> BrowseCatalogAsync(
        string? search,
        Guid? categoryId,
        bool includeUnpublished,
        CancellationToken cancellationToken = default)
    {
        var query = _context.CatalogItems.AsNoTracking().Where(c => !c.IsArchived);

        if (!includeUnpublished)
        {
            query = query.Where(c => c.Status == CatalogItemStatus.Published);
        }

        if (categoryId is not null)
        {
            query = query.Where(c => c.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c =>
                c.Name.Contains(term)
                || c.ShortDescription.Contains(term)
                || c.Code.Contains(term));
        }

        return await query
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CatalogItemSummaryDto(
                c.Id, c.Code, c.Name, c.ShortDescription, c.CategoryId, c.Category!.Name,
                c.Status, c.Cost, c.EstimatedDeliveryDays, c.Icon, c.RequiresApproval, c.SortOrder))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CatalogItemDetailDto?> GetCatalogItemAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var item = await _context.CatalogItems
            .AsNoTracking()
            .Include(c => c.Variables)
            .Include(c => c.Category)
            .Include(c => c.FulfilmentGroup)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return null;
        }

        string? approverName = null;

        if (item.ApproverUserId is not null)
        {
            approverName = await _context.Users.AsNoTracking()
                .Where(u => u.Id == item.ApproverUserId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (item.ApproverGroupId is not null)
        {
            approverName = await _context.Groups.AsNoTracking()
                .Where(g => g.Id == item.ApproverGroupId)
                .Select(g => g.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (item.ApprovalTargetKind == ApprovalTargetKind.Manager)
        {
            approverName = "The requester's line manager";
        }

        return new CatalogItemDetailDto(
            item.Id, item.Code, item.Name, item.ShortDescription, item.Description,
            item.CategoryId, item.Category?.Name, item.Status, item.Priority, item.Cost,
            item.EstimatedDeliveryDays, item.Icon, item.MaxQuantity, item.RequiresApproval,
            item.ApprovalTargetKind, approverName, item.FulfilmentGroupId,
            item.FulfilmentGroup?.Name,
            item.Variables
                .OrderBy(v => v.SortOrder)
                .Select(ToVariableDto)
                .ToList(),
            item.RowVersion);
    }

    // -----------------------------------------------------------------
    // Projection helpers
    // -----------------------------------------------------------------

    private static CatalogVariableDto ToVariableDto(CatalogItemVariable v)
        => new(v.Id, v.Key, v.Label, v.HelpText, v.Type, v.IsRequired, v.SortOrder,
            v.DefaultValue, v.Choices(), v.MaxLength, v.MinValue, v.MaxValue);

    private IQueryable<ApprovalDto> ProjectApprovals(IQueryable<Approval> query)
        => query.Select(a => new ApprovalDto(
            a.Id, a.Stage, a.Rule, a.TargetKind,
            a.ApproverUserId, a.ApproverUser!.DisplayName,
            a.ApproverGroupId, a.ApproverGroup!.Name,
            a.State, a.DecidedByUserId, null, a.DecidedAt, a.Comment,
            a.RecordLabel, a.Module, a.RecordId,
            _context.ServiceRequests.Where(r => r.Id == a.RecordId).Select(r => r.Number).FirstOrDefault()));

    private async Task<RequestDetailDto> ProjectAsync(ServiceRequest request, CancellationToken cancellationToken)
    {
        var names = await ResolveNamesAsync(request, cancellationToken).ConfigureAwait(false);

        var approvals = await ProjectApprovals(
                _context.Approvals.AsNoTracking()
                    .Where(a => a.Module == ApprovalModule && a.RecordId == request.Id)
                    .OrderBy(a => a.Stage))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var groupNames = await _context.Groups.AsNoTracking()
            .Where(g => request.Items.Select(i => i.FulfilmentGroupId).Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name, cancellationToken)
            .ConfigureAwait(false);

        var assigneeNames = await _context.Users.AsNoTracking()
            .Where(u => request.Items.Select(i => i.AssignedToUserId).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken)
            .ConfigureAwait(false);

        var items = request.Items
            .OrderBy(i => i.CreatedAt)
            .Select(i => new RequestItemDto(
                i.Id, i.CatalogItemId, i.CatalogItemName, i.Quantity, i.UnitCost, i.LineCost,
                i.Status, i.VariableValues(),
                i.FulfilmentGroupId,
                i.FulfilmentGroupId is not null && groupNames.TryGetValue(i.FulfilmentGroupId.Value, out var gn) ? gn : null,
                i.AssignedToUserId,
                i.AssignedToUserId is not null && assigneeNames.TryGetValue(i.AssignedToUserId.Value, out var an) ? an : null,
                i.FulfilledAt, i.FulfilmentNotes))
            .ToList();

        return new RequestDetailDto(
            request.Id, request.Number, request.Title, request.Description, request.Status,
            request.PendingReason, request.Channel, request.Priority,
            request.RequesterId, names.Requester,
            request.RequestedForId, names.RequestedFor,
            request.CategoryId, names.Category,
            request.FulfilmentGroupId, names.Group,
            request.AssignedToUserId, names.Assignee,
            request.RequiredByDate, request.SubmittedAt, request.ApprovedAt, request.FulfilledAt,
            request.ClosedAt, request.RejectionReason, request.CancellationReason,
            request.TotalCost, request.HasBreachedSla, request.NextSlaDueAt, request.CreatedAt,
            items,
            approvals,
            RequestStateMachine.AllowedTransitionsFrom(request.Status).ToList(),
            request.RowVersion);
    }

    private async Task<(string Requester, string RequestedFor, string? Assignee, string? Group, string? Category)>
        ResolveNamesAsync(ServiceRequest request, CancellationToken cancellationToken)
    {
        var userIds = new List<Guid> { request.RequesterId, request.RequestedForId };
        if (request.AssignedToUserId is not null)
        {
            userIds.Add(request.AssignedToUserId.Value);
        }

        var users = await _context.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken)
            .ConfigureAwait(false);

        var groupName = request.FulfilmentGroupId is null
            ? null
            : await _context.Groups.AsNoTracking()
                .Where(g => g.Id == request.FulfilmentGroupId)
                .Select(g => g.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        var categoryName = request.CategoryId is null
            ? null
            : await _context.Categories.AsNoTracking()
                .Where(c => c.Id == request.CategoryId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return (
            users.GetValueOrDefault(request.RequesterId, "Unknown"),
            users.GetValueOrDefault(request.RequestedForId, "Unknown"),
            request.AssignedToUserId is null ? null : users.GetValueOrDefault(request.AssignedToUserId.Value),
            groupName,
            categoryName);
    }

    // -----------------------------------------------------------------
    // Filtering and sorting
    // -----------------------------------------------------------------

    private IQueryable<ServiceRequest> ApplyFilters(IQueryable<ServiceRequest> source, RequestQuery query)
    {
        var userId = _currentUser.UserIdOrNull ?? Guid.Empty;
        var groupIds = _currentUser.GroupIds.ToList();

        source = query.Scope?.ToLowerInvariant() switch
        {
            "my-work" => source.Where(r => r.AssignedToUserId == userId && OpenStatuses.Contains(r.Status)),
            "raised-by-me" => source.Where(r => r.RequesterId == userId),
            "for-me" => source.Where(r => r.RequestedForId == userId),
            "my-groups" => source.Where(r =>
                r.FulfilmentGroupId != null && groupIds.Contains(r.FulfilmentGroupId.Value)
                && OpenStatuses.Contains(r.Status)),
            "unassigned" => source.Where(r => r.AssignedToUserId == null && OpenStatuses.Contains(r.Status)),
            "awaiting-approval" => source.Where(r => r.Status == RequestStatus.AwaitingApproval),

            // Deliberately restricted to open statuses, so a fulfilled request that breached
            // historically does not sit in the live breach queue for ever.
            "breached" => source.Where(r => r.HasBreachedSla && OpenStatuses.Contains(r.Status)),
            _ => source
        };

        if (query.Statuses is { Count: > 0 })
        {
            source = source.Where(r => query.Statuses.Contains(r.Status));
        }

        if (query.Priorities is { Count: > 0 })
        {
            source = source.Where(r => query.Priorities.Contains(r.Priority));
        }

        if (query.AssignedToUserId is not null)
        {
            source = source.Where(r => r.AssignedToUserId == query.AssignedToUserId);
        }

        if (query.FulfilmentGroupId is not null)
        {
            source = source.Where(r => r.FulfilmentGroupId == query.FulfilmentGroupId);
        }

        if (query.RequesterId is not null)
        {
            source = source.Where(r => r.RequesterId == query.RequesterId);
        }

        if (query.RequestedForId is not null)
        {
            source = source.Where(r => r.RequestedForId == query.RequestedForId);
        }

        if (query.BreachedOnly == true)
        {
            source = source.Where(r => r.HasBreachedSla);
        }

        if (query.OpenOnly == true)
        {
            source = source.Where(r => OpenStatuses.Contains(r.Status));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(r =>
                r.Number.Contains(term) || r.Title.Contains(term) || r.Description.Contains(term));
        }

        return source;
    }

    /// <summary>
    /// Sort fields are matched against a closed set by the service before this runs, so nothing
    /// here is ever interpolated from caller input.
    /// </summary>
    private static IQueryable<ServiceRequest> ApplySort(IQueryable<ServiceRequest> source, RequestQuery query)
        => (query.SortBy.ToLowerInvariant(), query.SortDescending) switch
        {
            ("number", true) => source.OrderByDescending(r => r.Number),
            ("number", false) => source.OrderBy(r => r.Number),
            ("priority", true) => source.OrderByDescending(r => r.Priority).ThenByDescending(r => r.CreatedAt),
            ("priority", false) => source.OrderBy(r => r.Priority).ThenByDescending(r => r.CreatedAt),
            ("status", true) => source.OrderByDescending(r => r.Status).ThenByDescending(r => r.CreatedAt),
            ("status", false) => source.OrderBy(r => r.Status).ThenByDescending(r => r.CreatedAt),
            ("nextsladueat", true) => source.OrderByDescending(r => r.NextSlaDueAt),
            ("nextsladueat", false) => source.OrderBy(r => r.NextSlaDueAt),
            ("totalcost", true) => source.OrderByDescending(r => r.TotalCost),
            ("totalcost", false) => source.OrderBy(r => r.TotalCost),
            (_, false) => source.OrderBy(r => r.CreatedAt),
            _ => source.OrderByDescending(r => r.CreatedAt)
        };
}
