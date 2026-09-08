using Microsoft.AspNetCore.Mvc;
using NexaOps.Api.Authorization;
using NexaOps.Application.Changes;
using NexaOps.Application.Common;
using NexaOps.Application.Security;
using NexaOps.Domain.Changes;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Api.Controllers;

/// <summary>
/// Change management: assessment, approval, scheduling, implementation and review.
/// <para>
/// The type of change decides how it is authorised - a standard change proceeds on a
/// pre-approved procedure, a normal change goes to the advisory board, and an emergency change
/// proceeds now and is reviewed afterwards.
/// </para>
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/changes")]
[Asp.Versioning.ApiVersion("1.0")]
[Produces("application/json")]
public sealed class ChangesController : ControllerBase
{
    private readonly IChangeService _changes;

    public ChangesController(IChangeService changes) => _changes = changes;

    /// <summary>Searches changes, and serves the change calendar via the window filters.</summary>
    [HttpGet]
    [RequiresPermission(Permissions.ChangeRead)]
    [ProducesResponseType(typeof(PagedResult<ChangeSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ChangeSummaryDto>>> Search(
        [FromQuery] ChangeSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await _changes.SearchAsync(request.ToQuery(), cancellationToken));
    }

    /// <summary>Counters for the change view, including the emergency change rate.</summary>
    [HttpGet("summary")]
    [RequiresPermission(Permissions.ChangeRead)]
    [ProducesResponseType(typeof(ChangeSummaryCountsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChangeSummaryCountsDto>> Summary(CancellationToken cancellationToken)
        => Ok(await _changes.GetSummaryAsync(cancellationToken));

    /// <summary>Full detail, including approvals and any colliding windows.</summary>
    [HttpGet("{id:guid}")]
    [RequiresPermission(Permissions.ChangeRead)]
    [ProducesResponseType(typeof(ChangeDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChangeDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _changes.GetAsync(id, cancellationToken));

    /// <summary>Looks a change up by its human-facing number, e.g. CHG0000042.</summary>
    [HttpGet("by-number/{number}")]
    [RequiresPermission(Permissions.ChangeRead)]
    [ProducesResponseType(typeof(ChangeDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChangeDetailDto>> GetByNumber(string number, CancellationToken cancellationToken)
        => Ok(await _changes.GetByNumberAsync(number, cancellationToken));

    /// <summary>Raises a change. An emergency change needs its own permission.</summary>
    [HttpPost]
    [RequiresPermission(Permissions.ChangeCreate)]
    [ProducesResponseType(typeof(ChangeDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ChangeDetailDto>> Create(
        [FromBody] CreateChangeCommand command,
        CancellationToken cancellationToken)
    {
        var created = await _changes.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id, version = "1.0" }, created);
    }

    /// <summary>Edits the change, its plans and its risk assessment.</summary>
    [HttpPatch("{id:guid}")]
    [RequiresPermission(Permissions.ChangeUpdate)]
    [ProducesResponseType(typeof(ChangeDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ChangeDetailDto>> Update(
        Guid id,
        [FromBody] UpdateChangeCommand command,
        CancellationToken cancellationToken)
        => Ok(await _changes.UpdateAsync(id, command, cancellationToken));

    /// <summary>Books or moves the implementation window.</summary>
    [HttpPost("{id:guid}/schedule")]
    [RequiresPermission(Permissions.ChangeSchedule)]
    [ProducesResponseType(typeof(ChangeDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ChangeDetailDto>> Schedule(
        Guid id,
        [FromBody] ScheduleChangeCommand command,
        CancellationToken cancellationToken)
        => Ok(await _changes.ScheduleAsync(id, command, cancellationToken));

    /// <summary>Sets the implementation group and assignee.</summary>
    [HttpPost("{id:guid}/assign")]
    [RequiresPermission(Permissions.ChangeAssign)]
    [ProducesResponseType(typeof(ChangeDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChangeDetailDto>> Assign(
        Guid id,
        [FromBody] AssignChangeCommand command,
        CancellationToken cancellationToken)
        => Ok(await _changes.AssignAsync(id, command, cancellationToken));

    /// <summary>Submits a normal change to the change advisory board.</summary>
    /// <remarks>
    /// Refused for standard and emergency changes, which are authorised by other means.
    /// </remarks>
    [HttpPost("{id:guid}/submit")]
    [RequiresPermission(Permissions.ChangeUpdate)]
    [ProducesResponseType(typeof(ChangeDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ChangeDetailDto>> Submit(Guid id, CancellationToken cancellationToken)
        => Ok(await _changes.SubmitForApprovalAsync(id, cancellationToken));

    /// <summary>Moves the change through its lifecycle, enforcing the state machine.</summary>
    /// <remarks>
    /// An implemented change cannot skip its review, and a change cannot close without a
    /// recorded outcome.
    /// </remarks>
    [HttpPost("{id:guid}/status")]
    [RequiresPermission(Permissions.ChangeRead)]
    [ProducesResponseType(typeof(ChangeDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ChangeDetailDto>> ChangeStatus(
        Guid id,
        [FromBody] ChangeStatusCommand command,
        CancellationToken cancellationToken)
        => Ok(await _changes.ChangeStatusAsync(id, command, cancellationToken));

    /// <summary>Records the post-implementation review and outcome.</summary>
    [HttpPost("{id:guid}/review")]
    [RequiresPermission(Permissions.ChangeReview)]
    [ProducesResponseType(typeof(ChangeDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ChangeDetailDto>> Review(
        Guid id,
        [FromBody] ReviewChangeCommand command,
        CancellationToken cancellationToken)
        => Ok(await _changes.ReviewAsync(id, command, cancellationToken));

    /// <summary>Cancels a change that will not proceed.</summary>
    [HttpPost("{id:guid}/cancel")]
    [RequiresPermission(Permissions.ChangeCancel)]
    [ProducesResponseType(typeof(ChangeDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChangeDetailDto>> Cancel(
        Guid id,
        [FromBody] CancelChangeCommand command,
        CancellationToken cancellationToken)
        => Ok(await _changes.CancelAsync(id, command, cancellationToken));

    /// <summary>Implementation notes. Internal notes are excluded at the query level.</summary>
    [HttpGet("{id:guid}/comments")]
    [RequiresPermission(Permissions.ChangeRead)]
    [ProducesResponseType(typeof(IReadOnlyList<ChangeCommentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChangeCommentDto>>> GetComments(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await _changes.GetCommentsAsync(id, cancellationToken));

    /// <summary>Adds an implementation note or a publicly visible comment.</summary>
    [HttpPost("{id:guid}/comments")]
    [RequiresPermission(Permissions.ChangeCommentCreate)]
    [ProducesResponseType(typeof(ChangeCommentDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ChangeCommentDto>> AddComment(
        Guid id,
        [FromBody] AddChangeCommentCommand command,
        CancellationToken cancellationToken)
    {
        var created = await _changes.AddCommentAsync(id, command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }
}

/// <summary>Query-string binding for the change queue and calendar.</summary>
public sealed class ChangeSearchRequest
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? Type { get; set; }
    public string? Risk { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public bool? OpenOnly { get; set; }
    public DateTimeOffset? WindowFrom { get; set; }
    public DateTimeOffset? WindowTo { get; set; }
    public string? Scope { get; set; }
    public string SortBy { get; set; } = "plannedStartAt";
    public bool SortDescending { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;

    public ChangeQuery ToQuery() => new()
    {
        Search = Search,
        Statuses = ParseList<ChangeStatus>(Status),
        Types = ParseList<ChangeType>(Type),
        Risks = ParseList<ChangeRisk>(Risk),
        AssignedToUserId = AssignedToUserId,
        OpenOnly = OpenOnly,
        WindowFrom = WindowFrom,
        WindowTo = WindowTo,
        Scope = Scope,
        SortBy = SortBy,
        SortDescending = SortDescending,
        Page = Page < 1 ? 1 : Page,
        PageSize = PageSize is < 1 or > 200 ? 25 : PageSize
    };

    /// <summary>
    /// Unparseable enum values are dropped rather than failing the request, so a stale bookmark
    /// degrades to a wider result set instead of an error page.
    /// </summary>
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
