using FluentValidation;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Incidents;
using NexaOps.Application.Security;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Common;
using NexaOps.Domain.Knowledge;

namespace NexaOps.Application.Knowledge;

/// <summary>
/// The only way a knowledge article changes.
/// </summary>
public interface IKnowledgeService
{
    Task<PagedResult<ArticleSummaryDto>> SearchAsync(ArticleQuery query, CancellationToken ct = default);

    /// <summary>Reads an article and records the view, which is what deflection is measured from.</summary>
    Task<ArticleDetailDto> GetAsync(Guid id, CancellationToken ct = default);

    Task<ArticleDetailDto> GetByNumberAsync(string number, CancellationToken ct = default);

    Task<KnowledgeSummaryCountsDto> GetSummaryAsync(CancellationToken ct = default);

    Task<ArticleDetailDto> CreateAsync(CreateArticleCommand command, CancellationToken ct = default);

    Task<ArticleDetailDto> UpdateAsync(Guid id, UpdateArticleCommand command, CancellationToken ct = default);

    Task<ArticleDetailDto> ChangeStatusAsync(Guid id, ChangeArticleStatusCommand command, CancellationToken ct = default);

    Task<ArticleDetailDto> RecordFeedbackAsync(Guid id, ArticleFeedbackCommand command, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class KnowledgeService : IKnowledgeService
{
    private const string EntityType = nameof(KnowledgeArticle);
    private const string NumberSequenceKey = "KB";
    private const int DefaultReviewIntervalDays = 365;

    private static readonly string[] SortableFields =
        ["relevance", "createdAt", "publishedAt", "title", "viewCount", "reviewDueAt"];

    private readonly IKnowledgeRepository _articles;
    private readonly IKnowledgeQueryService _queries;
    private readonly IServiceDeskReferenceRepository _reference;
    private readonly INumberSequenceService _numbers;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<KnowledgeService> _logger;

    public KnowledgeService(
        IKnowledgeRepository articles,
        IKnowledgeQueryService queries,
        IServiceDeskReferenceRepository reference,
        INumberSequenceService numbers,
        IAuditService audit,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<KnowledgeService> logger)
    {
        _articles = articles;
        _queries = queries;
        _reference = reference;
        _numbers = numbers;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<PagedResult<ArticleSummaryDto>> SearchAsync(ArticleQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        _currentUser.DemandPermission(Permissions.KnowledgeRead);

        if (!SortableFields.Contains(query.SortBy, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(query.SortBy),
                    $"Sort field must be one of: {string.Join(", ", SortableFields)}.")
            ]);
        }

        return _queries.SearchAsync(query, ct);
    }

    /// <inheritdoc />
    public async Task<ArticleDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.KnowledgeRead);

        var dto = await _queries.GetAsync(id, ct).ConfigureAwait(false)
                  ?? throw new EntityNotFoundException(EntityType, id);

        // The view is recorded on the tracked entity in the same unit of work as the read.
        // Deflection reporting is the module's justification, so the count has to be real.
        var article = await _articles.GetAsync(id, ct).ConfigureAwait(false);
        if (article is not null)
        {
            article.RecordView();
            await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return dto;
    }

    /// <inheritdoc />
    public async Task<ArticleDetailDto> GetByNumberAsync(string number, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.KnowledgeRead);

        return await _queries.GetByNumberAsync(number, ct).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(EntityType, number);
    }

    /// <inheritdoc />
    public Task<KnowledgeSummaryCountsDto> GetSummaryAsync(CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.KnowledgeRead);
        return _queries.GetSummaryAsync(ct);
    }

    /// <inheritdoc />
    public async Task<ArticleDetailDto> CreateAsync(CreateArticleCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.KnowledgeCreate);

        if (string.IsNullOrWhiteSpace(command.Title))
        {
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(nameof(command.Title), "A title is required.")
            ]);
        }

        if (command.CategoryId is not null)
        {
            _ = await _reference.GetCategoryAsync(command.CategoryId.Value, ct).ConfigureAwait(false)
                ?? throw new EntityNotFoundException("Category", command.CategoryId.Value);
        }

        var number = await _numbers.NextAsync(NumberSequenceKey, ct).ConfigureAwait(false);

        var article = new KnowledgeArticle
        {
            Number = number,
            Title = command.Title.Trim(),
            Summary = command.Summary?.Trim() ?? string.Empty,
            Body = command.Body?.Trim() ?? string.Empty,
            Keywords = command.Keywords?.Trim(),
            CategoryId = command.CategoryId,
            Audience = command.Audience,
            ProblemId = command.ProblemId,
            SourceIncidentId = command.SourceIncidentId,
            AuthorId = _currentUser.UserId,
            Status = ArticleStatus.Draft
        };

        _articles.Add(article);

        _audit.Record(AuditAction.Create, EntityType, article.Id.ToString(), article.Number,
            $"Drafted {article.Number}.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(article.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ArticleDetailDto> UpdateAsync(Guid id, UpdateArticleCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.KnowledgeUpdate);

        var article = await LoadAsync(id, ct).ConfigureAwait(false);
        ApplyConcurrencyToken(article, command.RowVersion);

        if (!string.IsNullOrWhiteSpace(command.Title))
        {
            article.Title = command.Title.Trim();
        }

        if (command.Summary is not null)
        {
            article.Summary = command.Summary.Trim();
        }

        if (command.Body is not null)
        {
            article.Body = command.Body.Trim();
        }

        if (command.Keywords is not null)
        {
            article.Keywords = command.Keywords.Trim();
        }

        if (command.CategoryId is not null)
        {
            _ = await _reference.GetCategoryAsync(command.CategoryId.Value, ct).ConfigureAwait(false)
                ?? throw new EntityNotFoundException("Category", command.CategoryId.Value);

            article.CategoryId = command.CategoryId;
        }

        if (command.Audience is not null)
        {
            article.Audience = command.Audience.Value;
        }

        _audit.Record(AuditAction.Update, EntityType, article.Id.ToString(), article.Number, "Article edited.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(article.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ArticleDetailDto> ChangeStatusAsync(
        Guid id,
        ChangeArticleStatusCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Publishing puts an article in front of the whole organisation, so it is separate from
        // writing one: an author cannot self-publish unreviewed guidance.
        _currentUser.DemandPermission(command.Status switch
        {
            ArticleStatus.Published => Permissions.KnowledgePublish,
            ArticleStatus.Retired => Permissions.KnowledgeRetire,
            _ => Permissions.KnowledgeUpdate
        });

        var article = await LoadAsync(id, ct).ConfigureAwait(false);
        ApplyConcurrencyToken(article, command.RowVersion);

        var previous = article.Status;
        var now = _clock.UtcNow;

        if (command.Status == ArticleStatus.Retired)
        {
            article.Retire(command.Reason, _currentUser.UserId, now);
        }
        else
        {
            if (command.Status == ArticleStatus.Published)
            {
                article.ReviewerId ??= _currentUser.UserId;
            }

            article.TransitionTo(
                command.Status,
                _currentUser.UserId,
                now,
                command.ReviewIntervalDays ?? DefaultReviewIntervalDays);
        }

        _audit.Record(
            AuditAction.Update,
            EntityType,
            article.Id.ToString(),
            article.Number,
            $"Status changed from {previous} to {article.Status}." +
            (string.IsNullOrWhiteSpace(command.Reason) ? string.Empty : $" {command.Reason.Trim()}"));

        if (article.Status == ArticleStatus.Published)
        {
            _logger.LogInformation(
                "Article {Number} published for {Audience}, review due {ReviewDue:u}.",
                article.Number, article.Audience, article.ReviewDueAt);
        }

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(article.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ArticleDetailDto> RecordFeedbackAsync(
        Guid id,
        ArticleFeedbackCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.KnowledgeFeedback);

        var article = await LoadAsync(id, ct).ConfigureAwait(false);

        if (!KnowledgeStateMachine.IsReadable(article.Status))
        {
            throw new DomainException(
                "knowledge.not_published",
                "Only a published article can be rated.");
        }

        var existing = await _articles
            .GetFeedbackAsync(article.Id, _currentUser.UserId, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _articles.AddFeedback(new ArticleFeedback
            {
                ArticleId = article.Id,
                UserId = _currentUser.UserId,
                WasHelpful = command.WasHelpful,
                Comment = string.IsNullOrWhiteSpace(command.Comment) ? null : command.Comment.Trim()
            });

            article.RecordFeedback(command.WasHelpful);
        }
        else if (existing.WasHelpful != command.WasHelpful)
        {
            // A change of mind moves the vote rather than adding a second one.
            if (command.WasHelpful)
            {
                article.HelpfulCount++;
                article.NotHelpfulCount = Math.Max(0, article.NotHelpfulCount - 1);
            }
            else
            {
                article.NotHelpfulCount++;
                article.HelpfulCount = Math.Max(0, article.HelpfulCount - 1);
            }

            existing.WasHelpful = command.WasHelpful;
            existing.Comment = string.IsNullOrWhiteSpace(command.Comment) ? null : command.Comment.Trim();
        }
        else
        {
            existing.Comment = string.IsNullOrWhiteSpace(command.Comment) ? existing.Comment : command.Comment.Trim();
        }

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(article.Id, ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private async Task<KnowledgeArticle> LoadAsync(Guid id, CancellationToken ct)
        => await _articles.GetAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(EntityType, id);

    private async Task<ArticleDetailDto> ReloadAsync(Guid id, CancellationToken ct)
        => await _queries.GetAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(EntityType, id);

    private void ApplyConcurrencyToken(KnowledgeArticle article, byte[]? rowVersion)
    {
        if (rowVersion is { Length: > 0 })
        {
            _articles.SetExpectedVersion(article, rowVersion);
        }
    }
}
