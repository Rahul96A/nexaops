using Microsoft.AspNetCore.Mvc;
using NexaOps.Api.Authorization;
using NexaOps.Application.Common;
using NexaOps.Application.Requests;
using NexaOps.Application.Security;
using NexaOps.Domain.Requests;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Api.Controllers;

/// <summary>
/// Service request management.
/// <para>
/// Tenant-scoped from the authenticated identity. Authorization is enforced twice - by the
/// attribute here and again inside the application service - because the service is also
/// reachable from the approval flow and, later, the workflow engine.
/// </para>
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/requests")]
[Asp.Versioning.ApiVersion("1.0")]
[Produces("application/json")]
public sealed class RequestsController : ControllerBase
{
    private readonly IRequestService _requests;

    public RequestsController(IRequestService requests) => _requests = requests;

    /// <summary>Searches service requests with filtering, sorting and paging.</summary>
    /// <remarks>
    /// A caller without <c>request.read.all</c> sees only requests they raised, that are for
    /// them, that are assigned to them, or that sit in one of their fulfilment groups.
    /// </remarks>
    [HttpGet]
    [RequiresPermission(Permissions.RequestRead)]
    [ProducesResponseType(typeof(PagedResult<RequestSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<RequestSummaryDto>>> Search(
        [FromQuery] RequestSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await _requests.SearchAsync(request.ToQuery(), cancellationToken));
    }

    /// <summary>Counters for the request side of the dashboard. Every figure is a live query.</summary>
    [HttpGet("summary")]
    [RequiresPermission(Permissions.RequestRead)]
    [ProducesResponseType(typeof(RequestSummaryCountsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RequestSummaryCountsDto>> Summary(CancellationToken cancellationToken)
        => Ok(await _requests.GetSummaryAsync(cancellationToken));

    /// <summary>Full detail for one request, including its lines and approvals.</summary>
    [HttpGet("{id:guid}")]
    [RequiresPermission(Permissions.RequestRead)]
    [ProducesResponseType(typeof(RequestDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RequestDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _requests.GetAsync(id, cancellationToken));

    /// <summary>Looks a request up by its human-facing number, e.g. REQ0000042.</summary>
    [HttpGet("by-number/{number}")]
    [RequiresPermission(Permissions.RequestRead)]
    [ProducesResponseType(typeof(RequestDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RequestDetailDto>> GetByNumber(string number, CancellationToken cancellationToken)
        => Ok(await _requests.GetByNumberAsync(number, cancellationToken));

    /// <summary>Raises a request for one or more catalogue items.</summary>
    /// <remarks>
    /// Every answer is validated server-side against the catalogue item's own field definitions.
    /// The request routes to approval or straight to fulfilment depending on what was ordered.
    /// </remarks>
    [HttpPost]
    [RequiresPermission(Permissions.RequestCreate)]
    [ProducesResponseType(typeof(RequestDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RequestDetailDto>> Create(
        [FromBody] CreateRequestCommand command,
        CancellationToken cancellationToken)
    {
        var created = await _requests.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id, version = "1.0" }, created);
    }

    /// <summary>Edits a request's own fields. Ordered lines are immutable once submitted.</summary>
    [HttpPatch("{id:guid}")]
    [RequiresPermission(Permissions.RequestUpdate)]
    [ProducesResponseType(typeof(RequestDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RequestDetailDto>> Update(
        Guid id,
        [FromBody] UpdateRequestCommand command,
        CancellationToken cancellationToken)
        => Ok(await _requests.UpdateAsync(id, command, cancellationToken));

    /// <summary>Sets the fulfilment group and assignee.</summary>
    [HttpPost("{id:guid}/assign")]
    [RequiresPermission(Permissions.RequestAssign)]
    [ProducesResponseType(typeof(RequestDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RequestDetailDto>> Assign(
        Guid id,
        [FromBody] AssignRequestCommand command,
        CancellationToken cancellationToken)
        => Ok(await _requests.AssignAsync(id, command, cancellationToken));

    /// <summary>Moves the request through its lifecycle, enforcing the state machine.</summary>
    /// <remarks>
    /// The permission required depends on the target: fulfilling, closing and cancelling are
    /// each distinct from a general edit.
    /// </remarks>
    [HttpPost("{id:guid}/status")]
    [RequiresPermission(Permissions.RequestRead)]
    [ProducesResponseType(typeof(RequestDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RequestDetailDto>> ChangeStatus(
        Guid id,
        [FromBody] ChangeRequestStatusCommand command,
        CancellationToken cancellationToken)
        => Ok(await _requests.ChangeStatusAsync(id, command, cancellationToken));

    /// <summary>Marks one ordered line delivered.</summary>
    [HttpPost("{id:guid}/items/{itemId:guid}/fulfil")]
    [RequiresPermission(Permissions.RequestFulfil)]
    [ProducesResponseType(typeof(RequestDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RequestDetailDto>> FulfilItem(
        Guid id,
        Guid itemId,
        [FromBody] FulfilRequestItemCommand command,
        CancellationToken cancellationToken)
        => Ok(await _requests.FulfilItemAsync(id, itemId, command, cancellationToken));

    /// <summary>Cancels a request.</summary>
    /// <remarks>
    /// A requester may withdraw their own request. Cancelling somebody else's needs
    /// <c>request.cancel</c>, which the service enforces.
    /// </remarks>
    [HttpPost("{id:guid}/cancel")]
    [RequiresPermission(Permissions.RequestRead)]
    [ProducesResponseType(typeof(RequestDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RequestDetailDto>> Cancel(
        Guid id,
        [FromBody] CancelRequestCommand command,
        CancellationToken cancellationToken)
        => Ok(await _requests.CancelAsync(id, command, cancellationToken));

    /// <summary>Correspondence on a request. Work notes are excluded at the query level.</summary>
    [HttpGet("{id:guid}/comments")]
    [RequiresPermission(Permissions.RequestRead)]
    [ProducesResponseType(typeof(IReadOnlyList<RequestCommentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RequestCommentDto>>> GetComments(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await _requests.GetCommentsAsync(id, cancellationToken));

    /// <summary>Adds a requester-visible comment or an internal work note.</summary>
    [HttpPost("{id:guid}/comments")]
    [RequiresPermission(Permissions.RequestRead)]
    [ProducesResponseType(typeof(RequestCommentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RequestCommentDto>> AddComment(
        Guid id,
        [FromBody] AddRequestCommentCommand command,
        CancellationToken cancellationToken)
    {
        var created = await _requests.AddCommentAsync(id, command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }
}

/// <summary>
/// The service catalogue.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/catalog")]
[Asp.Versioning.ApiVersion("1.0")]
[Produces("application/json")]
public sealed class CatalogController : ControllerBase
{
    private readonly ICatalogService _catalog;

    public CatalogController(ICatalogService catalog) => _catalog = catalog;

    /// <summary>Browses the catalogue. Unpublished items appear only for catalogue managers.</summary>
    [HttpGet("items")]
    [RequiresPermission(Permissions.CatalogRead)]
    [ProducesResponseType(typeof(IReadOnlyList<CatalogItemSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CatalogItemSummaryDto>>> Browse(
        [FromQuery] string? search,
        [FromQuery] Guid? categoryId,
        CancellationToken cancellationToken)
        => Ok(await _catalog.BrowseAsync(search, categoryId, cancellationToken));

    /// <summary>One catalogue item with the fields it asks for.</summary>
    [HttpGet("items/{id:guid}")]
    [RequiresPermission(Permissions.CatalogRead)]
    [ProducesResponseType(typeof(CatalogItemDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CatalogItemDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _catalog.GetAsync(id, cancellationToken));

    /// <summary>Creates a catalogue item as a draft.</summary>
    [HttpPost("items")]
    [RequiresPermission(Permissions.CatalogManage)]
    [ProducesResponseType(typeof(CatalogItemDetailDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CatalogItemDetailDto>> Create(
        [FromBody] UpsertCatalogItemCommand command,
        CancellationToken cancellationToken)
    {
        var created = await _catalog.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id, version = "1.0" }, created);
    }

    /// <summary>Edits a catalogue item and replaces its field definitions.</summary>
    [HttpPut("items/{id:guid}")]
    [RequiresPermission(Permissions.CatalogManage)]
    [ProducesResponseType(typeof(CatalogItemDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CatalogItemDetailDto>> Update(
        Guid id,
        [FromBody] UpsertCatalogItemCommand command,
        CancellationToken cancellationToken)
        => Ok(await _catalog.UpdateAsync(id, command, cancellationToken));

    /// <summary>Publishes an item so requesters can order it.</summary>
    /// <remarks>Refused when the item has no fulfilment group or no configured approver.</remarks>
    [HttpPost("items/{id:guid}/publish")]
    [RequiresPermission(Permissions.CatalogManage)]
    [ProducesResponseType(typeof(CatalogItemDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CatalogItemDetailDto>> Publish(Guid id, CancellationToken cancellationToken)
        => Ok(await _catalog.PublishAsync(id, cancellationToken));

    /// <summary>Withdraws an item. Existing requests keep their own snapshot and are unaffected.</summary>
    [HttpPost("items/{id:guid}/retire")]
    [RequiresPermission(Permissions.CatalogManage)]
    [ProducesResponseType(typeof(CatalogItemDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CatalogItemDetailDto>> Retire(Guid id, CancellationToken cancellationToken)
        => Ok(await _catalog.RetireAsync(id, cancellationToken));
}

/// <summary>
/// Approvals addressed to the caller.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/approvals")]
[Asp.Versioning.ApiVersion("1.0")]
[Produces("application/json")]
public sealed class ApprovalsController : ControllerBase
{
    private readonly IRequestService _requests;

    public ApprovalsController(IRequestService requests) => _requests = requests;

    /// <summary>
    /// The caller's approval queue. Without <c>approval.read.all</c> this returns only
    /// approvals addressed to them personally or through a group they belong to.
    /// </summary>
    [HttpGet]
    [RequiresPermission(Permissions.ApprovalAct)]
    [ProducesResponseType(typeof(IReadOnlyList<ApprovalDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ApprovalDto>>> Mine(
        [FromQuery] bool outstandingOnly = true,
        CancellationToken cancellationToken = default)
        => Ok(await _requests.GetMyApprovalsAsync(outstandingOnly, cancellationToken));

    /// <summary>
    /// Approves or rejects. A rejection requires a reason. A caller who is not the addressee
    /// gets 404, because confirming the approval exists discloses work they are not part of.
    /// </summary>
    [HttpPost("{id:guid}/decide")]
    [RequiresPermission(Permissions.ApprovalAct)]
    [ProducesResponseType(typeof(RequestDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RequestDetailDto>> Decide(
        Guid id,
        [FromBody] DecideApprovalCommand command,
        CancellationToken cancellationToken)
        => Ok(await _requests.DecideApprovalAsync(id, command, cancellationToken));
}

/// <summary>Query-string binding for the request queue.</summary>
public sealed class RequestSearchRequest
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public Guid? FulfilmentGroupId { get; set; }
    public Guid? RequesterId { get; set; }
    public Guid? RequestedForId { get; set; }
    public bool? BreachedOnly { get; set; }
    public bool? OpenOnly { get; set; }
    public string? Scope { get; set; }
    public string SortBy { get; set; } = "createdAt";
    public bool SortDescending { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;

    /// <summary>
    /// Turns the flat query string into a typed query. Unparseable enum values are dropped
    /// rather than failing the whole request, so a stale bookmark degrades to a wider result
    /// set instead of an error page.
    /// </summary>
    public RequestQuery ToQuery() => new()
    {
        Search = Search,
        Statuses = ParseList<RequestStatus>(Status),
        Priorities = ParseList<Priority>(Priority),
        AssignedToUserId = AssignedToUserId,
        FulfilmentGroupId = FulfilmentGroupId,
        RequesterId = RequesterId,
        RequestedForId = RequestedForId,
        BreachedOnly = BreachedOnly,
        OpenOnly = OpenOnly,
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
