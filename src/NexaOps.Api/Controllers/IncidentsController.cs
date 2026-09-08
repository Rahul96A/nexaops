using Microsoft.AspNetCore.Mvc;
using NexaOps.Api.Authorization;
using NexaOps.Application.Common;
using NexaOps.Application.Incidents;
using NexaOps.Application.Security;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Api.Controllers;

/// <summary>
/// Incident management.
/// <para>
/// Every endpoint is tenant-scoped from the authenticated identity. Authorization is enforced
/// twice - once by the attribute here and again inside the application service - because the
/// service is also reachable from the workflow engine and from confirmed AI actions.
/// </para>
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/incidents")]
[Asp.Versioning.ApiVersion("1.0")]
[Produces("application/json")]
public sealed class IncidentsController : ControllerBase
{
    private readonly IIncidentService _incidents;

    public IncidentsController(IIncidentService incidents) => _incidents = incidents;

    /// <summary>Searches incidents with filtering, sorting and paging.</summary>
    /// <remarks>
    /// A caller without <c>incident.read.all</c> sees only incidents they raised, are affected
    /// by, are assigned, or that sit in one of their assignment groups.
    /// </remarks>
    [HttpGet]
    [RequiresPermission(Permissions.IncidentRead)]
    [ProducesResponseType(typeof(PagedResult<IncidentListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<IncidentListItemDto>>> Search(
        [FromQuery] IncidentSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _incidents.SearchAsync(request.ToQuery(), cancellationToken);
        return Ok(result);
    }

    /// <summary>Counters for the service desk dashboard. Every figure is a live query.</summary>
    [HttpGet("summary")]
    [RequiresPermission(Permissions.IncidentRead)]
    [ProducesResponseType(typeof(ServiceDeskSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ServiceDeskSummaryDto>> Summary(CancellationToken cancellationToken)
        => Ok(await _incidents.GetServiceDeskSummaryAsync(cancellationToken));

    /// <summary>Full detail for one incident.</summary>
    [HttpGet("{id:guid}")]
    [RequiresPermission(Permissions.IncidentRead)]
    [ProducesResponseType(typeof(IncidentDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IncidentDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _incidents.GetAsync(id, cancellationToken));

    /// <summary>Full detail for one incident, looked up by its number (e.g. INC0001042).</summary>
    [HttpGet("by-number/{number}")]
    [RequiresPermission(Permissions.IncidentRead)]
    [ProducesResponseType(typeof(IncidentDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IncidentDetailDto>> GetByNumber(
        string number,
        CancellationToken cancellationToken)
        => Ok(await _incidents.GetByNumberAsync(number, cancellationToken));

    /// <summary>Raises a new incident.</summary>
    [HttpPost]
    [RequiresPermission(Permissions.IncidentCreate)]
    [ProducesResponseType(typeof(IncidentDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IncidentDetailDto>> Create(
        [FromBody] CreateIncidentCommand command,
        CancellationToken cancellationToken)
    {
        var incident = await _incidents.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = incident.Id, version = "1.0" }, incident);
    }

    /// <summary>Updates incident fields. Omitted fields are left unchanged.</summary>
    [HttpPatch("{id:guid}")]
    [RequiresPermission(Permissions.IncidentUpdate)]
    [ProducesResponseType(typeof(IncidentDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IncidentDetailDto>> Update(
        Guid id,
        [FromBody] UpdateIncidentCommand command,
        CancellationToken cancellationToken)
        => Ok(await _incidents.UpdateAsync(id, command, cancellationToken));

    /// <summary>Sets the assignment group and assignee.</summary>
    [HttpPost("{id:guid}/assign")]
    [RequiresPermission(Permissions.IncidentAssign)]
    [ProducesResponseType(typeof(IncidentDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<IncidentDetailDto>> Assign(
        Guid id,
        [FromBody] AssignIncidentCommand command,
        CancellationToken cancellationToken)
        => Ok(await _incidents.AssignAsync(id, command, cancellationToken));

    /// <summary>
    /// Moves the incident through its lifecycle. The permission required depends on the target
    /// status: resolving needs <c>incident.resolve</c>, closing <c>incident.close</c>, and so on,
    /// which the application service enforces.
    /// </summary>
    [HttpPost("{id:guid}/status")]
    [RequiresPermission(Permissions.IncidentUpdate)]
    [ProducesResponseType(typeof(IncidentDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IncidentDetailDto>> ChangeStatus(
        Guid id,
        [FromBody] ChangeIncidentStatusCommand command,
        CancellationToken cancellationToken)
        => Ok(await _incidents.ChangeStatusAsync(id, command, cancellationToken));

    /// <summary>
    /// Re-derives priority from impact and urgency, or applies an explicit override.
    /// An override additionally requires <c>incident.priority.override</c> and a reason.
    /// </summary>
    [HttpPost("{id:guid}/priority")]
    [RequiresPermission(Permissions.IncidentUpdate)]
    [ProducesResponseType(typeof(IncidentDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<IncidentDetailDto>> ChangePriority(
        Guid id,
        [FromBody] ChangeIncidentPriorityCommand command,
        CancellationToken cancellationToken)
        => Ok(await _incidents.ChangePriorityAsync(id, command, cancellationToken));

    /// <summary>Declares or withdraws major incident status.</summary>
    [HttpPost("{id:guid}/major")]
    [RequiresPermission(Permissions.IncidentDeclareMajor)]
    [ProducesResponseType(typeof(IncidentDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<IncidentDetailDto>> DeclareMajor(
        Guid id,
        [FromBody] DeclareMajorIncidentCommand command,
        CancellationToken cancellationToken)
        => Ok(await _incidents.DeclareMajorAsync(id, command, cancellationToken));

    /// <summary>
    /// Comments and work notes, oldest first. Work notes are excluded for callers without
    /// <c>incident.worknote.read</c> - they are filtered out in the query, not the serialiser.
    /// </summary>
    [HttpGet("{id:guid}/comments")]
    [RequiresPermission(Permissions.IncidentRead)]
    [ProducesResponseType(typeof(IReadOnlyList<IncidentCommentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<IncidentCommentDto>>> GetComments(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await _incidents.GetCommentsAsync(id, cancellationToken));

    /// <summary>Adds a comment or, with the right permission, an internal work note.</summary>
    [HttpPost("{id:guid}/comments")]
    [RequiresPermission(Permissions.IncidentCommentCreate)]
    [ProducesResponseType(typeof(IncidentCommentDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<IncidentCommentDto>> AddComment(
        Guid id,
        [FromBody] AddIncidentCommentCommand command,
        CancellationToken cancellationToken)
    {
        var comment = await _incidents.AddCommentAsync(id, command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, comment);
    }

    /// <summary>Merged activity timeline: comments, work notes and audited field changes.</summary>
    [HttpGet("{id:guid}/activity")]
    [RequiresPermission(Permissions.IncidentRead)]
    [ProducesResponseType(typeof(IReadOnlyList<IncidentActivityDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<IncidentActivityDto>>> GetActivity(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await _incidents.GetActivityAsync(id, cancellationToken));

    /// <summary>Archives an incident. Records are never hard-deleted.</summary>
    [HttpDelete("{id:guid}")]
    [RequiresPermission(Permissions.IncidentArchive)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(
        Guid id,
        [FromBody] ArchiveIncidentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _incidents.ArchiveAsync(id, request.Reason, cancellationToken);
        return NoContent();
    }
}

/// <summary>Reason for archiving, recorded in the audit trail.</summary>
public sealed class ArchiveIncidentRequest
{
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Query-string shape for the incident list.
/// <para>
/// Kept separate from <see cref="IncidentQuery"/> so that the wire format uses repeated scalar
/// parameters, which is what model binding and OpenAPI handle cleanly, rather than exposing the
/// internal query object directly.
/// </para>
/// </summary>
public sealed class IncidentSearchRequest
{
    public string? Search { get; set; }
    public IncidentViewScope Scope { get; set; } = IncidentViewScope.All;

    /// <summary>Repeat the parameter to filter on several statuses.</summary>
    public IncidentStatus[]? Status { get; set; }

    /// <summary>Repeat the parameter to filter on several priorities.</summary>
    public Priority[]? Priority { get; set; }

    public bool? OpenOnly { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public Guid? AssignmentGroupId { get; set; }
    public Guid? RequesterId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? SubcategoryId { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? DepartmentId { get; set; }
    public bool? IsMajorIncident { get; set; }
    public bool? HasBreachedSla { get; set; }
    public DateTimeOffset? CreatedFrom { get; set; }
    public DateTimeOffset? CreatedTo { get; set; }
    public string? Tag { get; set; }
    public bool IncludeArchived { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = PagedQuery.DefaultPageSize;
    public string? SortBy { get; set; }
    public SortDirection SortDirection { get; set; } = SortDirection.Descending;

    public IncidentQuery ToQuery() => new()
    {
        Search = Search,
        Scope = Scope,
        Statuses = Status,
        Priorities = Priority,
        OpenOnly = OpenOnly,
        AssignedToUserId = AssignedToUserId,
        AssignmentGroupId = AssignmentGroupId,
        RequesterId = RequesterId,
        CategoryId = CategoryId,
        SubcategoryId = SubcategoryId,
        OrganizationId = OrganizationId,
        DepartmentId = DepartmentId,
        IsMajorIncident = IsMajorIncident,
        HasBreachedSla = HasBreachedSla,
        CreatedFrom = CreatedFrom,
        CreatedTo = CreatedTo,
        Tag = Tag,
        IncludeArchived = IncludeArchived,
        Page = Page,
        PageSize = PageSize,
        SortBy = SortBy,
        SortDirection = SortDirection
    };
}
