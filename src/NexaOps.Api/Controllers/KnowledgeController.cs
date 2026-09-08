using Microsoft.AspNetCore.Mvc;
using NexaOps.Api.Authorization;
using NexaOps.Application.Common;
using NexaOps.Application.Knowledge;
using NexaOps.Application.Security;
using NexaOps.Domain.Knowledge;

namespace NexaOps.Api.Controllers;

/// <summary>
/// The knowledge base.
/// <para>
/// A reader without <c>knowledge.read.internal</c> sees only published articles written for
/// everyone. Drafts and service-desk runbooks are invisible rather than merely unopenable.
/// </para>
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/knowledge")]
[Asp.Versioning.ApiVersion("1.0")]
[Produces("application/json")]
public sealed class KnowledgeController : ControllerBase
{
    private readonly IKnowledgeService _knowledge;

    public KnowledgeController(IKnowledgeService knowledge) => _knowledge = knowledge;

    /// <summary>Searches articles by title, summary, body and keywords.</summary>
    [HttpGet]
    [RequiresPermission(Permissions.KnowledgeRead)]
    [ProducesResponseType(typeof(PagedResult<ArticleSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ArticleSummaryDto>>> Search(
        [FromQuery] ArticleSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await _knowledge.SearchAsync(request.ToQuery(), cancellationToken));
    }

    /// <summary>Counters for the knowledge view, including how many articles have gone stale.</summary>
    [HttpGet("summary")]
    [RequiresPermission(Permissions.KnowledgeRead)]
    [ProducesResponseType(typeof(KnowledgeSummaryCountsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<KnowledgeSummaryCountsDto>> Summary(CancellationToken cancellationToken)
        => Ok(await _knowledge.GetSummaryAsync(cancellationToken));

    /// <summary>Reads one article. Records the view, which is what deflection is measured from.</summary>
    [HttpGet("{id:guid}")]
    [RequiresPermission(Permissions.KnowledgeRead)]
    [ProducesResponseType(typeof(ArticleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ArticleDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _knowledge.GetAsync(id, cancellationToken));

    /// <summary>Looks an article up by its human-facing number, e.g. KB0000042.</summary>
    [HttpGet("by-number/{number}")]
    [RequiresPermission(Permissions.KnowledgeRead)]
    [ProducesResponseType(typeof(ArticleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ArticleDetailDto>> GetByNumber(string number, CancellationToken cancellationToken)
        => Ok(await _knowledge.GetByNumberAsync(number, cancellationToken));

    /// <summary>Drafts a new article.</summary>
    [HttpPost]
    [RequiresPermission(Permissions.KnowledgeCreate)]
    [ProducesResponseType(typeof(ArticleDetailDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ArticleDetailDto>> Create(
        [FromBody] CreateArticleCommand command,
        CancellationToken cancellationToken)
    {
        var created = await _knowledge.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id, version = "1.0" }, created);
    }

    /// <summary>Edits an article's content and classification.</summary>
    [HttpPatch("{id:guid}")]
    [RequiresPermission(Permissions.KnowledgeUpdate)]
    [ProducesResponseType(typeof(ArticleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ArticleDetailDto>> Update(
        Guid id,
        [FromBody] UpdateArticleCommand command,
        CancellationToken cancellationToken)
        => Ok(await _knowledge.UpdateAsync(id, command, cancellationToken));

    /// <summary>Moves the article through its lifecycle.</summary>
    /// <remarks>
    /// Publishing needs <c>knowledge.publish</c>, separate from writing, so an author cannot
    /// self-publish unreviewed guidance. Publishing also sets the next review date.
    /// </remarks>
    [HttpPost("{id:guid}/status")]
    [RequiresPermission(Permissions.KnowledgeRead)]
    [ProducesResponseType(typeof(ArticleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ArticleDetailDto>> ChangeStatus(
        Guid id,
        [FromBody] ChangeArticleStatusCommand command,
        CancellationToken cancellationToken)
        => Ok(await _knowledge.ChangeStatusAsync(id, command, cancellationToken));

    /// <summary>Records whether the article helped. A reader may change their mind.</summary>
    [HttpPost("{id:guid}/feedback")]
    [RequiresPermission(Permissions.KnowledgeFeedback)]
    [ProducesResponseType(typeof(ArticleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ArticleDetailDto>> Feedback(
        Guid id,
        [FromBody] ArticleFeedbackCommand command,
        CancellationToken cancellationToken)
        => Ok(await _knowledge.RecordFeedbackAsync(id, command, cancellationToken));
}

/// <summary>Query-string binding for knowledge search.</summary>
public sealed class ArticleSearchRequest
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? AuthorId { get; set; }
    public string? Scope { get; set; }
    public string SortBy { get; set; } = "relevance";
    public bool SortDescending { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;

    public ArticleQuery ToQuery() => new()
    {
        Search = Search,
        Statuses = ParseList<ArticleStatus>(Status),
        CategoryId = CategoryId,
        AuthorId = AuthorId,
        Scope = Scope,
        SortBy = SortBy,
        SortDescending = SortDescending,
        Page = Page < 1 ? 1 : Page,
        PageSize = PageSize is < 1 or > 200 ? 25 : PageSize
    };

    private static List<T>? ParseList<T>(string? value) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parsed = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => Enum.TryParse<T>(part, ignoreCase: true, out var result) ? result : (T?)null)
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .ToList();

        return parsed.Count == 0 ? null : parsed;
    }
}
