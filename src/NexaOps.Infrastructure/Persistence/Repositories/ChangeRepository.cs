using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Changes;
using NexaOps.Application.Common;
using NexaOps.Application.Requests;
using NexaOps.Application.Security;
using NexaOps.Domain.Approvals;
using NexaOps.Domain.Changes;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Infrastructure.Persistence.Repositories;

/// <inheritdoc />
public sealed class ChangeRepository : IChangeRepository
{
    private readonly NexaOpsDbContext _context;

    public ChangeRepository(NexaOpsDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<Change?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.Changes
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Change?> GetByNumberAsync(string number, CancellationToken cancellationToken = default)
        => await _context.Changes
            .FirstOrDefaultAsync(c => c.Number == number, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(Change change) => _context.Changes.Add(change);

    /// <inheritdoc />
    public void AddComment(ChangeComment comment) => _context.ChangeComments.Add(comment);

    /// <inheritdoc />
    public void SetExpectedVersion(Change change, byte[] rowVersion)
        => _context.Entry(change).Property(c => c.RowVersion).OriginalValue = rowVersion;
}

/// <inheritdoc />
public sealed class ChangeQueryService : IChangeQueryService
{
    private const string ApprovalModule = "Change";

    private static readonly ChangeStatus[] OpenStatuses =
    [
        ChangeStatus.Draft, ChangeStatus.Assessing, ChangeStatus.AwaitingApproval,
        ChangeStatus.Scheduled, ChangeStatus.Implementing, ChangeStatus.Review
    ];

    private readonly NexaOpsDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public ChangeQueryService(
        NexaOpsDbContext context,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>
    /// Changes are visible to everyone in the tenant who holds <c>change.read</c>.
    /// <para>
    /// Knowing what is being changed to the services you depend on is not privileged
    /// information, and a change calendar that only the change team can see is not a calendar.
    /// Internal implementation notes are still filtered separately.
    /// </para>
    /// </summary>
    private IQueryable<Change> VisibleChanges() => _context.Changes.AsNoTracking();

    /// <inheritdoc />
    public async Task<PagedResult<ChangeSummaryDto>> SearchAsync(
        ChangeQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var source = ApplyFilters(VisibleChanges(), query);

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0)
        {
            return PagedResult<ChangeSummaryDto>.Empty(query.Page, query.PageSize);
        }

        var items = await ApplySort(source, query)
            .Skip((Math.Max(query.Page, 1) - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToSummary)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<ChangeSummaryDto>(items, total, query.Page, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<ChangeDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var change = await VisibleChanges()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return change is null ? null : await ProjectAsync(change, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ChangeDetailDto?> GetByNumberAsync(string number, CancellationToken cancellationToken = default)
    {
        var change = await VisibleChanges()
            .FirstOrDefaultAsync(c => c.Number == number, cancellationToken)
            .ConfigureAwait(false);

        return change is null ? null : await ProjectAsync(change, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChangeCommentDto>?> GetCommentsAsync(
        Guid changeId,
        CancellationToken cancellationToken = default)
    {
        var exists = await VisibleChanges()
            .AnyAsync(c => c.Id == changeId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            return null;
        }

        var query = _context.ChangeComments
            .AsNoTracking()
            .Where(c => c.ChangeId == changeId);

        if (!_currentUser.HasPermission(Permissions.ChangeWorkNoteRead))
        {
            query = query.Where(c => c.Kind == IncidentCommentKind.PublicComment);
        }

        return await query
            .OrderBy(c => c.CreatedAt)
            .Select(c => new ChangeCommentDto(
                c.Id, c.Kind, c.Body, c.AuthorId, c.AuthorDisplayName, c.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ChangeSummaryCountsDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserIdOrNull ?? Guid.Empty;
        var now = _clock.UtcNow;
        var weekEnd = now.AddDays(7);
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var visible = VisibleChanges();
        var open = visible.Where(c => OpenStatuses.Contains(c.Status));

        var openCount = await open.CountAsync(cancellationToken).ConfigureAwait(false);

        var awaitingApproval = await open
            .CountAsync(c => c.Status == ChangeStatus.AwaitingApproval, cancellationToken)
            .ConfigureAwait(false);

        var scheduledThisWeek = await open
            .CountAsync(
                c => c.Status == ChangeStatus.Scheduled
                     && c.PlannedStartAt != null
                     && c.PlannedStartAt >= now
                     && c.PlannedStartAt <= weekEnd,
                cancellationToken)
            .ConfigureAwait(false);

        var implementing = await open
            .CountAsync(c => c.Status == ChangeStatus.Implementing, cancellationToken)
            .ConfigureAwait(false);

        var awaitingReview = await open
            .CountAsync(c => c.Status == ChangeStatus.Review, cancellationToken)
            .ConfigureAwait(false);

        var assignedToMe = await open
            .CountAsync(c => c.AssignedToUserId == userId, cancellationToken)
            .ConfigureAwait(false);

        // Emergency change rate is the number a change manager watches: a rising count means the
        // normal process is not working for people.
        var emergencyThisMonth = await visible
            .CountAsync(c => c.Type == ChangeType.Emergency && c.CreatedAt >= monthStart, cancellationToken)
            .ConfigureAwait(false);

        var byStatus = await open
            .GroupBy(c => c.Status)
            .Select(g => new ChangeStatusCountDto(g.Key, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ChangeSummaryCountsDto(
            openCount, awaitingApproval, scheduledThisWeek, implementing,
            awaitingReview, assignedToMe, emergencyThisMonth, byStatus);
    }

    // -----------------------------------------------------------------
    // Projection, filtering and sorting
    // -----------------------------------------------------------------

    /// <summary>
    /// An expression, not a method.
    /// <para>
    /// A static method here compiles and looks identical, but EF cannot translate a method call
    /// into SQL: it materialises the rows first and runs the projection in memory, where the
    /// unloaded AssignedTo navigation is null and dereferencing it throws. As an expression it
    /// becomes a LEFT JOIN and a null name, which is what the DTO expects.
    /// </para>
    /// </summary>
    private static readonly Expression<Func<Change, ChangeSummaryDto>> ToSummary =
        c => new ChangeSummaryDto(
            c.Id, c.Number, c.Title, c.Type, c.Status, c.Risk, c.Priority, c.Outcome,
            c.AssignedToUserId, c.AssignedTo!.DisplayName, c.AssignedTo!.AvatarColor,
            c.AssignmentGroupId, c.AssignmentGroup!.Name,
            c.PlannedStartAt, c.PlannedEndAt, c.RequiresDowntime, c.CreatedAt);

    private async Task<ChangeDetailDto> ProjectAsync(Change change, CancellationToken cancellationToken)
    {
        var approvals = await _context.Approvals.AsNoTracking()
            .Where(a => a.Module == ApprovalModule && a.RecordId == change.Id)
            .OrderBy(a => a.Stage)
            .Select(a => new ApprovalDto(
                a.Id, a.Stage, a.Rule, a.TargetKind,
                a.ApproverUserId, a.ApproverUser!.DisplayName,
                a.ApproverGroupId, a.ApproverGroup!.Name,
                a.State, a.DecidedByUserId, null, a.DecidedAt, a.Comment,
                a.RecordLabel, a.Module, a.RecordId, change.Number))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Collisions are surfaced as a warning, never as a block: two changes in one window is
        // sometimes exactly the intent, and the person scheduling is better placed to judge.
        var colliding = change.PlannedStartAt is null || change.PlannedEndAt is null
            ? []
            : await VisibleChanges()
                .Where(c => c.Id != change.Id
                            && OpenStatuses.Contains(c.Status)
                            && c.PlannedStartAt != null
                            && c.PlannedEndAt != null
                            && c.PlannedStartAt < change.PlannedEndAt
                            && change.PlannedStartAt < c.PlannedEndAt)
                .OrderBy(c => c.PlannedStartAt)
                .Take(10)
                .Select(ToSummary)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var names = await ResolveNamesAsync(change, cancellationToken).ConfigureAwait(false);

        var problemNumber = change.ProblemId is null
            ? null
            : await _context.Problems.AsNoTracking()
                .Where(p => p.Id == change.ProblemId)
                .Select(p => p.Number)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return new ChangeDetailDto(
            change.Id, change.Number, change.Title, change.Description,
            change.Type, change.Status, change.Risk, change.Impact, change.Priority,
            change.ImplementationPlan, change.RollbackPlan, change.TestPlan, change.ImpactAssessment,
            change.PlannedStartAt, change.PlannedEndAt, change.ActualStartAt, change.ActualEndAt,
            change.RequiresDowntime,
            change.AssignmentGroupId, names.Group,
            change.AssignedToUserId, names.Assignee,
            change.RequestedByUserId, names.RequestedBy,
            change.CategoryId, names.Category,
            change.ProblemId, problemNumber,
            change.Outcome, change.ReviewNotes, change.RejectionReason, change.CancellationReason,
            change.ApprovedAt, change.ReviewedAt, change.ClosedAt, change.CreatedAt,
            approvals,
            colliding,
            ChangeStateMachine.AllowedTransitionsFrom(change.Status).ToList(),
            change.RowVersion);
    }

    private async Task<(string RequestedBy, string? Assignee, string? Group, string? Category)>
        ResolveNamesAsync(Change change, CancellationToken cancellationToken)
    {
        var userIds = new List<Guid> { change.RequestedByUserId };
        if (change.AssignedToUserId is not null)
        {
            userIds.Add(change.AssignedToUserId.Value);
        }

        var users = await _context.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken)
            .ConfigureAwait(false);

        var groupName = change.AssignmentGroupId is null
            ? null
            : await _context.Groups.AsNoTracking()
                .Where(g => g.Id == change.AssignmentGroupId)
                .Select(g => g.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        var categoryName = change.CategoryId is null
            ? null
            : await _context.Categories.AsNoTracking()
                .Where(c => c.Id == change.CategoryId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return (
            users.GetValueOrDefault(change.RequestedByUserId, "Unknown"),
            change.AssignedToUserId is null ? null : users.GetValueOrDefault(change.AssignedToUserId.Value),
            groupName,
            categoryName);
    }

    private IQueryable<Change> ApplyFilters(IQueryable<Change> source, ChangeQuery query)
    {
        var userId = _currentUser.UserIdOrNull ?? Guid.Empty;
        var now = _clock.UtcNow;

        source = query.Scope?.ToLowerInvariant() switch
        {
            "my-work" => source.Where(c => c.AssignedToUserId == userId && OpenStatuses.Contains(c.Status)),
            "awaiting-approval" => source.Where(c => c.Status == ChangeStatus.AwaitingApproval),
            "awaiting-review" => source.Where(c => c.Status == ChangeStatus.Review),
            "this-week" => source.Where(c =>
                c.PlannedStartAt != null
                && c.PlannedStartAt >= now
                && c.PlannedStartAt <= now.AddDays(7)),
            "emergency" => source.Where(c => c.Type == ChangeType.Emergency),
            _ => source
        };

        if (query.Statuses is { Count: > 0 })
        {
            source = source.Where(c => query.Statuses.Contains(c.Status));
        }

        if (query.Types is { Count: > 0 })
        {
            source = source.Where(c => query.Types.Contains(c.Type));
        }

        if (query.Risks is { Count: > 0 })
        {
            source = source.Where(c => query.Risks.Contains(c.Risk));
        }

        if (query.AssignedToUserId is not null)
        {
            source = source.Where(c => c.AssignedToUserId == query.AssignedToUserId);
        }

        if (query.OpenOnly == true)
        {
            source = source.Where(c => OpenStatuses.Contains(c.Status));
        }

        // The calendar asks for changes whose window intersects the range, not ones that start
        // inside it: a long change spanning the whole week must still appear.
        if (query.WindowFrom is not null)
        {
            source = source.Where(c => c.PlannedEndAt == null || c.PlannedEndAt >= query.WindowFrom);
        }

        if (query.WindowTo is not null)
        {
            source = source.Where(c => c.PlannedStartAt == null || c.PlannedStartAt <= query.WindowTo);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(c =>
                c.Number.Contains(term) || c.Title.Contains(term) || c.Description.Contains(term));
        }

        return source;
    }

    /// <summary>Sort fields are matched against a closed set by the service before this runs.</summary>
    private static IQueryable<Change> ApplySort(IQueryable<Change> source, ChangeQuery query)
        => (query.SortBy.ToLowerInvariant(), query.SortDescending) switch
        {
            ("number", true) => source.OrderByDescending(c => c.Number),
            ("number", false) => source.OrderBy(c => c.Number),
            ("risk", true) => source.OrderByDescending(c => c.Risk).ThenBy(c => c.PlannedStartAt),
            ("risk", false) => source.OrderBy(c => c.Risk).ThenBy(c => c.PlannedStartAt),
            ("status", true) => source.OrderByDescending(c => c.Status),
            ("status", false) => source.OrderBy(c => c.Status),
            ("priority", true) => source.OrderByDescending(c => c.Priority),
            ("priority", false) => source.OrderBy(c => c.Priority),
            ("createdat", true) => source.OrderByDescending(c => c.CreatedAt),
            ("createdat", false) => source.OrderBy(c => c.CreatedAt),

            // The calendar is the default view, so unscheduled changes sort last rather than
            // first: a null window is not "the earliest window".
            (_, true) => source.OrderByDescending(c => c.PlannedStartAt == null)
                .ThenByDescending(c => c.PlannedStartAt),
            _ => source.OrderBy(c => c.PlannedStartAt == null).ThenBy(c => c.PlannedStartAt)
        };
}
