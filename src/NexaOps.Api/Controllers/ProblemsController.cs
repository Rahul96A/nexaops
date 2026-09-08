using Microsoft.AspNetCore.Mvc;
using NexaOps.Api.Authorization;
using NexaOps.Application.Common;
using NexaOps.Application.Problems;
using NexaOps.Application.Security;
using NexaOps.Domain.Problems;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Api.Controllers;

/// <summary>
/// Problem management: root cause investigation, known errors and permanent fixes.
/// <para>
/// Authorization is enforced twice - by the attribute here and again inside the application
/// service - because publishing a known error and resolving a problem will also be reachable
/// from the workflow engine.
/// </para>
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/problems")]
[Asp.Versioning.ApiVersion("1.0")]
[Produces("application/json")]
public sealed class ProblemsController : ControllerBase
{
    private readonly IProblemService _problems;

    public ProblemsController(IProblemService problems) => _problems = problems;

    /// <summary>Searches problems with filtering, sorting and paging.</summary>
    /// <remarks>
    /// Search covers root cause and workaround text as well as the title, because "has anyone
    /// seen this before" is the question an agent is actually asking.
    /// </remarks>
    [HttpGet]
    [RequiresPermission(Permissions.ProblemRead)]
    [ProducesResponseType(typeof(PagedResult<ProblemSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProblemSummaryDto>>> Search(
        [FromQuery] ProblemSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await _problems.SearchAsync(request.ToQuery(), cancellationToken));
    }

    /// <summary>Counters for the problem management view. Every figure is a live query.</summary>
    [HttpGet("summary")]
    [RequiresPermission(Permissions.ProblemRead)]
    [ProducesResponseType(typeof(ProblemSummaryCountsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProblemSummaryCountsDto>> Summary(CancellationToken cancellationToken)
        => Ok(await _problems.GetSummaryAsync(cancellationToken));

    /// <summary>Full detail for one problem, including the incidents it is believed to cause.</summary>
    [HttpGet("{id:guid}")]
    [RequiresPermission(Permissions.ProblemRead)]
    [ProducesResponseType(typeof(ProblemDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProblemDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _problems.GetAsync(id, cancellationToken));

    /// <summary>Looks a problem up by its human-facing number, e.g. PRB0000042.</summary>
    [HttpGet("by-number/{number}")]
    [RequiresPermission(Permissions.ProblemRead)]
    [ProducesResponseType(typeof(ProblemDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProblemDetailDto>> GetByNumber(string number, CancellationToken cancellationToken)
        => Ok(await _problems.GetByNumberAsync(number, cancellationToken));

    /// <summary>Raises a problem, optionally from the incident that prompted it.</summary>
    [HttpPost]
    [RequiresPermission(Permissions.ProblemCreate)]
    [ProducesResponseType(typeof(ProblemDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProblemDetailDto>> Create(
        [FromBody] CreateProblemCommand command,
        CancellationToken cancellationToken)
    {
        var created = await _problems.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id, version = "1.0" }, created);
    }

    /// <summary>Edits a problem's own fields and classification.</summary>
    [HttpPatch("{id:guid}")]
    [RequiresPermission(Permissions.ProblemUpdate)]
    [ProducesResponseType(typeof(ProblemDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProblemDetailDto>> Update(
        Guid id,
        [FromBody] UpdateProblemCommand command,
        CancellationToken cancellationToken)
        => Ok(await _problems.UpdateAsync(id, command, cancellationToken));

    /// <summary>Records root cause, confidence, workaround and permanent fix.</summary>
    /// <remarks>Recording findings on an untouched problem starts the investigation.</remarks>
    [HttpPost("{id:guid}/findings")]
    [RequiresPermission(Permissions.ProblemInvestigate)]
    [ProducesResponseType(typeof(ProblemDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProblemDetailDto>> RecordFindings(
        Guid id,
        [FromBody] RecordFindingsCommand command,
        CancellationToken cancellationToken)
        => Ok(await _problems.RecordFindingsAsync(id, command, cancellationToken));

    /// <summary>Sets the assignment group, assignee and accountable owner.</summary>
    [HttpPost("{id:guid}/assign")]
    [RequiresPermission(Permissions.ProblemAssign)]
    [ProducesResponseType(typeof(ProblemDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProblemDetailDto>> Assign(
        Guid id,
        [FromBody] AssignProblemCommand command,
        CancellationToken cancellationToken)
        => Ok(await _problems.AssignAsync(id, command, cancellationToken));

    /// <summary>Moves the problem through its lifecycle, enforcing the state machine.</summary>
    /// <remarks>
    /// Publishing a known error requires a root cause and a workaround; resolving requires a
    /// recorded permanent fix. The permission needed depends on the target status.
    /// </remarks>
    [HttpPost("{id:guid}/status")]
    [RequiresPermission(Permissions.ProblemRead)]
    [ProducesResponseType(typeof(ProblemDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ProblemDetailDto>> ChangeStatus(
        Guid id,
        [FromBody] ChangeProblemStatusCommand command,
        CancellationToken cancellationToken)
        => Ok(await _problems.ChangeStatusAsync(id, command, cancellationToken));

    /// <summary>Investigation notes. Internal notes are excluded at the query level.</summary>
    [HttpGet("{id:guid}/comments")]
    [RequiresPermission(Permissions.ProblemRead)]
    [ProducesResponseType(typeof(IReadOnlyList<ProblemCommentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProblemCommentDto>>> GetComments(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await _problems.GetCommentsAsync(id, cancellationToken));

    /// <summary>Adds an investigation note or a publicly visible comment.</summary>
    [HttpPost("{id:guid}/comments")]
    [RequiresPermission(Permissions.ProblemCommentCreate)]
    [ProducesResponseType(typeof(ProblemCommentDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ProblemCommentDto>> AddComment(
        Guid id,
        [FromBody] AddProblemCommentCommand command,
        CancellationToken cancellationToken)
    {
        var created = await _problems.AddCommentAsync(id, command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>Attributes an incident to this problem. Idempotent.</summary>
    [HttpPost("{id:guid}/incidents")]
    [RequiresPermission(Permissions.ProblemLinkIncident)]
    [ProducesResponseType(typeof(ProblemDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProblemDetailDto>> LinkIncident(
        Guid id,
        [FromBody] LinkIncidentCommand command,
        CancellationToken cancellationToken)
        => Ok(await _problems.LinkIncidentAsync(id, command, cancellationToken));

    /// <summary>Removes an incident's attribution to this problem.</summary>
    [HttpDelete("{id:guid}/incidents/{incidentId:guid}")]
    [RequiresPermission(Permissions.ProblemLinkIncident)]
    [ProducesResponseType(typeof(ProblemDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ProblemDetailDto>> UnlinkIncident(
        Guid id,
        Guid incidentId,
        CancellationToken cancellationToken)
        => Ok(await _problems.UnlinkIncidentAsync(id, incidentId, cancellationToken));
}

/// <summary>Query-string binding for the problem queue.</summary>
public sealed class ProblemSearchRequest
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public Guid? CategoryId { get; set; }
    public bool? OpenOnly { get; set; }
    public bool? KnownErrorsOnly { get; set; }
    public string? Scope { get; set; }
    public string SortBy { get; set; } = "createdAt";
    public bool SortDescending { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;

    /// <summary>
    /// Unparseable enum values are dropped rather than failing the whole request, so a stale
    /// bookmark degrades to a wider result set instead of an error page.
    /// </summary>
    public ProblemQuery ToQuery() => new()
    {
        Search = Search,
        Statuses = ParseList<ProblemStatus>(Status),
        Priorities = ParseList<Priority>(Priority),
        AssignedToUserId = AssignedToUserId,
        OwnerUserId = OwnerUserId,
        CategoryId = CategoryId,
        OpenOnly = OpenOnly,
        KnownErrorsOnly = KnownErrorsOnly,
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
