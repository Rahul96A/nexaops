using NexaOps.Application.Common;
using NexaOps.Domain.Knowledge;

namespace NexaOps.Application.Knowledge;

public sealed record ArticleSummaryDto(
    Guid Id,
    string Number,
    string Title,
    string Summary,
    ArticleStatus Status,
    ArticleAudience Audience,
    Guid? CategoryId,
    string? CategoryName,
    Guid AuthorId,
    string AuthorName,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ReviewDueAt,
    int ViewCount,

    /// <summary>Null when nobody has rated it. Not zero — that would condemn every new article.</summary>
    double? HelpfulRatio,
    DateTimeOffset CreatedAt);

public sealed record ArticleDetailDto(
    Guid Id,
    string Number,
    string Title,
    string Summary,
    string Body,
    string? Keywords,
    ArticleStatus Status,
    ArticleAudience Audience,
    Guid? CategoryId,
    string? CategoryName,
    Guid AuthorId,
    string AuthorName,
    Guid? ReviewerId,
    string? ReviewerName,
    Guid? ProblemId,
    string? ProblemNumber,
    DateTimeOffset? SubmittedForReviewAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ReviewDueAt,
    DateTimeOffset? RetiredAt,
    string? RetirementReason,
    int ViewCount,
    int HelpfulCount,
    int NotHelpfulCount,
    double? HelpfulRatio,

    /// <summary>The reader's own verdict, so the UI can show which button they pressed.</summary>
    bool? MyFeedback,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ArticleStatus> AllowedTransitions,
    byte[]? RowVersion);

public sealed record KnowledgeSummaryCountsDto(
    int Published,
    int Stale,
    int InReview,
    int Drafts,
    int MyDrafts,
    int TotalViews);

// ---------------------------------------------------------------------------
// Commands
// ---------------------------------------------------------------------------

public sealed class CreateArticleCommand
{
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Body { get; set; }
    public string? Keywords { get; set; }
    public Guid? CategoryId { get; set; }
    public ArticleAudience Audience { get; set; } = ArticleAudience.ServiceDesk;

    /// <summary>The problem whose workaround this documents.</summary>
    public Guid? ProblemId { get; set; }

    /// <summary>The incident that prompted writing it.</summary>
    public Guid? SourceIncidentId { get; set; }
}

public sealed class UpdateArticleCommand
{
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string? Body { get; set; }
    public string? Keywords { get; set; }
    public Guid? CategoryId { get; set; }
    public ArticleAudience? Audience { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class ChangeArticleStatusCommand
{
    public ArticleStatus Status { get; set; }

    /// <summary>Required when retiring, so the withdrawal is explainable.</summary>
    public string? Reason { get; set; }

    /// <summary>How long the article stays trusted once published. Defaults to a year.</summary>
    public int? ReviewIntervalDays { get; set; }

    public byte[]? RowVersion { get; set; }
}

public sealed class ArticleFeedbackCommand
{
    public bool WasHelpful { get; set; }
    public string? Comment { get; set; }
}

public sealed class ArticleQuery
{
    public string? Search { get; set; }
    public List<ArticleStatus>? Statuses { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? AuthorId { get; set; }

    /// <summary>Named scope, e.g. <c>my-drafts</c>, <c>needs-review</c>, <c>stale</c>.</summary>
    public string? Scope { get; set; }

    public string SortBy { get; set; } = "relevance";
    public bool SortDescending { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

/// <summary>Write-side access to knowledge articles.</summary>
public interface IKnowledgeRepository
{
    Task<KnowledgeArticle?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<KnowledgeArticle?> GetByNumberAsync(string number, CancellationToken cancellationToken = default);

    void Add(KnowledgeArticle article);

    void SetExpectedVersion(KnowledgeArticle article, byte[] rowVersion);

    /// <summary>The reader's existing verdict, so a change of mind updates rather than duplicates.</summary>
    Task<ArticleFeedback?> GetFeedbackAsync(
        Guid articleId,
        Guid userId,
        CancellationToken cancellationToken = default);

    void AddFeedback(ArticleFeedback feedback);

    /// <summary>
    /// Published articles whose review date has passed, across all tenants. Used only by the
    /// background sweep, which establishes each tenant scope before acting.
    /// </summary>
    Task<IReadOnlyList<KnowledgeArticle>> GetArticlesDueForReviewAsync(
        DateTimeOffset asOf,
        int maxResults,
        CancellationToken cancellationToken = default);
}

/// <summary>Read-side queries for the knowledge base.</summary>
public interface IKnowledgeQueryService
{
    Task<PagedResult<ArticleSummaryDto>> SearchAsync(
        ArticleQuery query,
        CancellationToken cancellationToken = default);

    Task<ArticleDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ArticleDetailDto?> GetByNumberAsync(string number, CancellationToken cancellationToken = default);

    Task<KnowledgeSummaryCountsDto> GetSummaryAsync(CancellationToken cancellationToken = default);
}
