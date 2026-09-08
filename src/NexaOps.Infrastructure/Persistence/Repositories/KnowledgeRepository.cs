using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Knowledge;
using NexaOps.Application.Security;
using NexaOps.Domain.Knowledge;

namespace NexaOps.Infrastructure.Persistence.Repositories;

/// <inheritdoc />
public sealed class KnowledgeRepository : IKnowledgeRepository
{
    private readonly NexaOpsDbContext _context;

    public KnowledgeRepository(NexaOpsDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<KnowledgeArticle?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.Articles
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<KnowledgeArticle?> GetByNumberAsync(string number, CancellationToken cancellationToken = default)
        => await _context.Articles
            .FirstOrDefaultAsync(a => a.Number == number, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(KnowledgeArticle article) => _context.Articles.Add(article);

    /// <inheritdoc />
    public void SetExpectedVersion(KnowledgeArticle article, byte[] rowVersion)
        => _context.Entry(article).Property(a => a.RowVersion).OriginalValue = rowVersion;

    /// <inheritdoc />
    public async Task<ArticleFeedback?> GetFeedbackAsync(
        Guid articleId,
        Guid userId,
        CancellationToken cancellationToken = default)
        => await _context.ArticleFeedback
            .FirstOrDefaultAsync(f => f.ArticleId == articleId && f.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void AddFeedback(ArticleFeedback feedback) => _context.ArticleFeedback.Add(feedback);

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeArticle>> GetArticlesDueForReviewAsync(
        DateTimeOffset asOf,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        // The review sweep genuinely spans tenants, exactly as the SLA monitor does. The caller
        // re-establishes a per-tenant scope before acting on any individual result.
        using var suppression = _context.SuppressTenantFilter();

        return await _context.Articles
            .Where(a => a.Status == ArticleStatus.Published
                        && a.ReviewDueAt != null
                        && a.ReviewDueAt <= asOf)
            .OrderBy(a => a.ReviewDueAt)
            .Take(Math.Clamp(maxResults, 1, 1000))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <inheritdoc />
public sealed class KnowledgeQueryService : IKnowledgeQueryService
{
    private readonly NexaOpsDbContext _context;
    private readonly ICurrentUser _currentUser;

    public KnowledgeQueryService(NexaOpsDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    /// <summary>
    /// What this reader may see.
    /// <para>
    /// A reader without <c>knowledge.read.internal</c> sees only published or stale articles
    /// written for everyone. Drafts and service-desk runbooks are invisible - not merely
    /// unopenable - so a requester cannot discover that an internal runbook exists.
    /// </para>
    /// </summary>
    private IQueryable<KnowledgeArticle> VisibleArticles()
    {
        var query = _context.Articles.AsNoTracking();

        if (_currentUser.HasPermission(Permissions.KnowledgeReadInternal))
        {
            return query;
        }

        return query.Where(a =>
            a.Audience == ArticleAudience.Everyone
            && (a.Status == ArticleStatus.Published || a.Status == ArticleStatus.Stale));
    }

    private static readonly Expression<Func<KnowledgeArticle, ArticleSummaryDto>> ToSummary =
        a => new ArticleSummaryDto(
            a.Id, a.Number, a.Title, a.Summary, a.Status, a.Audience,
            a.CategoryId, a.Category!.Name,
            a.AuthorId, a.Author!.DisplayName,
            a.PublishedAt, a.ReviewDueAt, a.ViewCount,
            a.HelpfulCount + a.NotHelpfulCount == 0
                ? null
                : (double)a.HelpfulCount / (a.HelpfulCount + a.NotHelpfulCount),
            a.CreatedAt);

    /// <inheritdoc />
    public async Task<PagedResult<ArticleSummaryDto>> SearchAsync(
        ArticleQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var source = ApplyFilters(VisibleArticles(), query);

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0)
        {
            return PagedResult<ArticleSummaryDto>.Empty(query.Page, query.PageSize);
        }

        var items = await ApplySort(source, query)
            .Skip((Math.Max(query.Page, 1) - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToSummary)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<ArticleSummaryDto>(items, total, query.Page, query.PageSize);
    }

    /// <inheritdoc />
    public Task<ArticleDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => ProjectDetail(VisibleArticles().Where(a => a.Id == id)).FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<ArticleDetailDto?> GetByNumberAsync(string number, CancellationToken cancellationToken = default)
        => ProjectDetail(VisibleArticles().Where(a => a.Number == number)).FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<KnowledgeSummaryCountsDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserIdOrNull ?? Guid.Empty;
        var visible = VisibleArticles();

        var published = await visible
            .CountAsync(a => a.Status == ArticleStatus.Published, cancellationToken)
            .ConfigureAwait(false);

        var stale = await visible
            .CountAsync(a => a.Status == ArticleStatus.Stale, cancellationToken)
            .ConfigureAwait(false);

        var inReview = await visible
            .CountAsync(a => a.Status == ArticleStatus.InReview, cancellationToken)
            .ConfigureAwait(false);

        var drafts = await visible
            .CountAsync(a => a.Status == ArticleStatus.Draft, cancellationToken)
            .ConfigureAwait(false);

        var myDrafts = await visible
            .CountAsync(a => a.Status == ArticleStatus.Draft && a.AuthorId == userId, cancellationToken)
            .ConfigureAwait(false);

        var totalViews = await visible
            .SumAsync(a => a.ViewCount, cancellationToken)
            .ConfigureAwait(false);

        return new KnowledgeSummaryCountsDto(published, stale, inReview, drafts, myDrafts, totalViews);
    }

    private IQueryable<ArticleDetailDto> ProjectDetail(IQueryable<KnowledgeArticle> source)
    {
        var userId = _currentUser.UserIdOrNull ?? Guid.Empty;

        return source.Select(a => new ArticleDetailDto(
            a.Id, a.Number, a.Title, a.Summary, a.Body, a.Keywords, a.Status, a.Audience,
            a.CategoryId, a.Category!.Name,
            a.AuthorId, a.Author!.DisplayName,
            a.ReviewerId, a.Reviewer!.DisplayName,
            a.ProblemId,
            _context.Problems.Where(p => p.Id == a.ProblemId).Select(p => p.Number).FirstOrDefault(),
            a.SubmittedForReviewAt, a.PublishedAt, a.ReviewDueAt, a.RetiredAt, a.RetirementReason,
            a.ViewCount, a.HelpfulCount, a.NotHelpfulCount,
            a.HelpfulCount + a.NotHelpfulCount == 0
                ? null
                : (double)a.HelpfulCount / (a.HelpfulCount + a.NotHelpfulCount),
            _context.ArticleFeedback
                .Where(f => f.ArticleId == a.Id && f.UserId == userId)
                .Select(f => (bool?)f.WasHelpful)
                .FirstOrDefault(),
            a.CreatedAt,
            KnowledgeStateMachine.AllowedTransitionsFrom(a.Status).ToList(),
            a.RowVersion));
    }

    private IQueryable<KnowledgeArticle> ApplyFilters(IQueryable<KnowledgeArticle> source, ArticleQuery query)
    {
        var userId = _currentUser.UserIdOrNull ?? Guid.Empty;

        source = query.Scope?.ToLowerInvariant() switch
        {
            "my-drafts" => source.Where(a => a.AuthorId == userId && a.Status == ArticleStatus.Draft),
            "needs-review" => source.Where(a => a.Status == ArticleStatus.InReview),
            "stale" => source.Where(a => a.Status == ArticleStatus.Stale),
            "published" => source.Where(a => a.Status == ArticleStatus.Published),
            _ => source
        };

        if (query.Statuses is { Count: > 0 })
        {
            source = source.Where(a => query.Statuses.Contains(a.Status));
        }

        if (query.CategoryId is not null)
        {
            source = source.Where(a => a.CategoryId == query.CategoryId);
        }

        if (query.AuthorId is not null)
        {
            source = source.Where(a => a.AuthorId == query.AuthorId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();

            // Keywords are searched because they exist precisely for the words a reader would
            // use that the article itself does not contain.
            source = source.Where(a =>
                a.Title.Contains(term)
                || a.Summary.Contains(term)
                || a.Body.Contains(term)
                || (a.Keywords != null && a.Keywords.Contains(term)));
        }

        return source;
    }

    /// <summary>Sort fields are matched against a closed set by the service before this runs.</summary>
    private static IQueryable<KnowledgeArticle> ApplySort(IQueryable<KnowledgeArticle> source, ArticleQuery query)
        => (query.SortBy.ToLowerInvariant(), query.SortDescending) switch
        {
            ("title", true) => source.OrderByDescending(a => a.Title),
            ("title", false) => source.OrderBy(a => a.Title),
            ("publishedat", false) => source.OrderBy(a => a.PublishedAt),
            ("publishedat", _) => source.OrderByDescending(a => a.PublishedAt),
            ("viewcount", false) => source.OrderBy(a => a.ViewCount),
            ("viewcount", _) => source.OrderByDescending(a => a.ViewCount),
            ("reviewdueat", _) => source.OrderBy(a => a.ReviewDueAt),
            ("createdat", false) => source.OrderBy(a => a.CreatedAt),
            ("createdat", _) => source.OrderByDescending(a => a.CreatedAt),

            // "Relevance" without a full-text index is an honest approximation: the articles
            // people actually use, most-read first. Naming it relevance rather than pretending
            // to a ranking function the database is not computing.
            _ => source.OrderByDescending(a => a.ViewCount).ThenByDescending(a => a.PublishedAt)
        };
}
