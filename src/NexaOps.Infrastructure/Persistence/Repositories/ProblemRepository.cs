using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Problems;
using NexaOps.Application.Security;
using NexaOps.Domain.Problems;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Infrastructure.Persistence.Repositories;

/// <inheritdoc />
public sealed class ProblemRepository : IProblemRepository
{
    private readonly NexaOpsDbContext _context;

    public ProblemRepository(NexaOpsDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<Problem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.Problems
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Problem?> GetByNumberAsync(string number, CancellationToken cancellationToken = default)
        => await _context.Problems
            .FirstOrDefaultAsync(p => p.Number == number, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(Problem problem) => _context.Problems.Add(problem);

    /// <inheritdoc />
    public void AddComment(ProblemComment comment) => _context.ProblemComments.Add(comment);

    /// <inheritdoc />
    public void SetExpectedVersion(Problem problem, byte[] rowVersion)
        => _context.Entry(problem).Property(p => p.RowVersion).OriginalValue = rowVersion;

    /// <inheritdoc />
    public async Task RefreshLinkedIncidentCountAsync(Guid problemId, CancellationToken cancellationToken = default)
    {
        var problem = await _context.Problems
            .FirstOrDefaultAsync(p => p.Id == problemId, cancellationToken)
            .ConfigureAwait(false);

        if (problem is null)
        {
            return;
        }

        // Counted from the incidents themselves rather than incremented, so a link made by any
        // other route - a future workflow step, a bulk import - still produces a correct figure.
        problem.LinkedIncidentCount = await _context.Incidents
            .CountAsync(i => i.ProblemId == problemId, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <inheritdoc />
public sealed class ProblemQueryService : IProblemQueryService
{
    private static readonly ProblemStatus[] OpenStatuses =
    [
        ProblemStatus.New, ProblemStatus.Investigating,
        ProblemStatus.KnownError, ProblemStatus.FixInProgress
    ];

    private readonly NexaOpsDbContext _context;
    private readonly ICurrentUser _currentUser;

    public ProblemQueryService(NexaOpsDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Problems are visible to everyone in the tenant who holds <c>problem.read</c>.
    /// <para>
    /// Deliberately unlike incidents and requests, which are scoped to the people involved. A
    /// known error exists precisely so that anybody fielding a call can find the workaround; a
    /// visibility rule that hid it from the person on the phone would defeat the module.
    /// Internal investigation notes are still filtered separately.
    /// </para>
    /// </summary>
    private IQueryable<Problem> VisibleProblems() => _context.Problems.AsNoTracking();

    /// <inheritdoc />
    public async Task<PagedResult<ProblemSummaryDto>> SearchAsync(
        ProblemQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var source = ApplyFilters(VisibleProblems(), query);

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0)
        {
            return PagedResult<ProblemSummaryDto>.Empty(query.Page, query.PageSize);
        }

        var items = await ApplySort(source, query)
            .Skip((Math.Max(query.Page, 1) - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new ProblemSummaryDto(
                p.Id, p.Number, p.Title, p.Status, p.Priority, p.Origin,
                p.AssignedToUserId, p.AssignedTo!.DisplayName, p.AssignedTo!.AvatarColor,
                p.OwnerUserId, p.Owner!.DisplayName,
                p.AssignmentGroupId, p.AssignmentGroup!.Name,
                p.CategoryId, p.Category!.Name,
                p.Workaround != null && p.Workaround != "",
                p.LinkedIncidentCount, p.IsMajorProblem, p.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<ProblemSummaryDto>(items, total, query.Page, query.PageSize);
    }

    /// <inheritdoc />
    public Task<ProblemDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => ProjectDetail(VisibleProblems().Where(p => p.Id == id)).FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<ProblemDetailDto?> GetByNumberAsync(string number, CancellationToken cancellationToken = default)
        => ProjectDetail(VisibleProblems().Where(p => p.Number == number)).FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProblemCommentDto>?> GetCommentsAsync(
        Guid problemId,
        CancellationToken cancellationToken = default)
    {
        var exists = await VisibleProblems()
            .AnyAsync(p => p.Id == problemId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            return null;
        }

        var query = _context.ProblemComments
            .AsNoTracking()
            .Where(c => c.ProblemId == problemId);

        // Investigation notes are excluded at the query level, not hidden in the browser.
        if (!_currentUser.HasPermission(Permissions.ProblemWorkNoteRead))
        {
            query = query.Where(c => c.Kind == IncidentCommentKind.PublicComment);
        }

        return await query
            .OrderBy(c => c.CreatedAt)
            .Select(c => new ProblemCommentDto(
                c.Id, c.Kind, c.Body, c.AuthorId, c.AuthorDisplayName, c.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProblemSummaryCountsDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserIdOrNull ?? Guid.Empty;
        var visible = VisibleProblems();
        var open = visible.Where(p => OpenStatuses.Contains(p.Status));

        var openCount = await open.CountAsync(cancellationToken).ConfigureAwait(false);

        var investigating = await open
            .CountAsync(p => p.Status == ProblemStatus.Investigating, cancellationToken)
            .ConfigureAwait(false);

        var knownErrors = await visible
            .CountAsync(p => p.Status == ProblemStatus.KnownError, cancellationToken)
            .ConfigureAwait(false);

        var assignedToMe = await open
            .CountAsync(p => p.AssignedToUserId == userId, cancellationToken)
            .ConfigureAwait(false);

        var ownedByMe = await open
            .CountAsync(p => p.OwnerUserId == userId, cancellationToken)
            .ConfigureAwait(false);

        var unassigned = await open
            .CountAsync(p => p.AssignedToUserId == null, cancellationToken)
            .ConfigureAwait(false);

        var major = await open
            .CountAsync(p => p.IsMajorProblem, cancellationToken)
            .ConfigureAwait(false);

        var byStatus = await open
            .GroupBy(p => p.Status)
            .Select(g => new ProblemStatusCountDto(g.Key, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ProblemSummaryCountsDto(
            openCount, investigating, knownErrors, assignedToMe, ownedByMe, unassigned, major, byStatus);
    }

    // -----------------------------------------------------------------
    // Projection, filtering and sorting
    // -----------------------------------------------------------------

    private IQueryable<ProblemDetailDto> ProjectDetail(IQueryable<Problem> source)
        => source.Select(p => new ProblemDetailDto(
            p.Id, p.Number, p.Title, p.Description, p.Status, p.Priority, p.Origin,
            p.CategoryId, p.Category!.Name,
            p.SubcategoryId, p.Subcategory!.Name,
            p.AssignmentGroupId, p.AssignmentGroup!.Name,
            p.AssignedToUserId, p.AssignedTo!.DisplayName,
            p.OwnerUserId, p.Owner!.DisplayName,
            p.RootCause, p.RootCauseConfidence, p.Workaround, p.PermanentFix,
            p.IsMajorProblem, p.LinkedIncidentCount,
            p.InvestigationStartedAt, p.KnownErrorAt, p.ResolvedAt, p.ClosedAt, p.CreatedAt,

            // The evidence for the problem. Ordered by priority so the worst incident it caused
            // is the first thing an investigator sees.
            _context.Incidents
                .Where(i => i.ProblemId == p.Id)
                .OrderBy(i => i.Priority).ThenByDescending(i => i.CreatedAt)
                .Select(i => new LinkedIncidentDto(
                    i.Id, i.Number, i.Title, i.Status, i.Priority, i.CreatedAt))
                .ToList(),

            ProblemStateMachine.AllowedTransitionsFrom(p.Status).ToList(),
            p.RowVersion));

    private IQueryable<Problem> ApplyFilters(IQueryable<Problem> source, ProblemQuery query)
    {
        var userId = _currentUser.UserIdOrNull ?? Guid.Empty;

        source = query.Scope?.ToLowerInvariant() switch
        {
            "my-work" => source.Where(p => p.AssignedToUserId == userId && OpenStatuses.Contains(p.Status)),
            "owned-by-me" => source.Where(p => p.OwnerUserId == userId && OpenStatuses.Contains(p.Status)),
            "known-errors" => source.Where(p => p.Status == ProblemStatus.KnownError),
            "unassigned" => source.Where(p => p.AssignedToUserId == null && OpenStatuses.Contains(p.Status)),
            "major" => source.Where(p => p.IsMajorProblem && OpenStatuses.Contains(p.Status)),
            _ => source
        };

        if (query.Statuses is { Count: > 0 })
        {
            source = source.Where(p => query.Statuses.Contains(p.Status));
        }

        if (query.Priorities is { Count: > 0 })
        {
            source = source.Where(p => query.Priorities.Contains(p.Priority));
        }

        if (query.AssignedToUserId is not null)
        {
            source = source.Where(p => p.AssignedToUserId == query.AssignedToUserId);
        }

        if (query.OwnerUserId is not null)
        {
            source = source.Where(p => p.OwnerUserId == query.OwnerUserId);
        }

        if (query.CategoryId is not null)
        {
            source = source.Where(p => p.CategoryId == query.CategoryId);
        }

        if (query.OpenOnly == true)
        {
            source = source.Where(p => OpenStatuses.Contains(p.Status));
        }

        if (query.KnownErrorsOnly == true)
        {
            source = source.Where(p => p.Status == ProblemStatus.KnownError);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();

            // Root cause and workaround are searched too, because "has anyone seen this before"
            // is the question an agent is actually asking.
            source = source.Where(p =>
                p.Number.Contains(term)
                || p.Title.Contains(term)
                || p.Description.Contains(term)
                || (p.RootCause != null && p.RootCause.Contains(term))
                || (p.Workaround != null && p.Workaround.Contains(term)));
        }

        return source;
    }

    /// <summary>Sort fields are matched against a closed set by the service before this runs.</summary>
    private static IQueryable<Problem> ApplySort(IQueryable<Problem> source, ProblemQuery query)
        => (query.SortBy.ToLowerInvariant(), query.SortDescending) switch
        {
            ("number", true) => source.OrderByDescending(p => p.Number),
            ("number", false) => source.OrderBy(p => p.Number),
            ("priority", true) => source.OrderByDescending(p => p.Priority).ThenByDescending(p => p.CreatedAt),
            ("priority", false) => source.OrderBy(p => p.Priority).ThenByDescending(p => p.CreatedAt),
            ("status", true) => source.OrderByDescending(p => p.Status).ThenByDescending(p => p.CreatedAt),
            ("status", false) => source.OrderBy(p => p.Status).ThenByDescending(p => p.CreatedAt),
            ("linkedincidentcount", false) => source.OrderBy(p => p.LinkedIncidentCount),
            ("linkedincidentcount", _) => source.OrderByDescending(p => p.LinkedIncidentCount),
            (_, false) => source.OrderBy(p => p.CreatedAt),
            _ => source.OrderByDescending(p => p.CreatedAt)
        };
}
