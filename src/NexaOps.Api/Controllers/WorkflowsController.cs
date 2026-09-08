using Microsoft.AspNetCore.Mvc;
using NexaOps.Api.Authorization;
using NexaOps.Application.Common;
using NexaOps.Application.Security;
using NexaOps.Application.Workflows;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Workflows;

namespace NexaOps.Api.Controllers;

/// <summary>
/// Automation rules and the record of what they have done.
/// <para>
/// Rules run in-process at the moment a record changes, not on a schedule. There is no endpoint
/// to run one on demand: a rule that can be fired by hand against an arbitrary record is a way
/// to reassign other people's work without holding the permission to do it.
/// </para>
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/workflows")]
[Asp.Versioning.ApiVersion("1.0")]
[Produces("application/json")]
public sealed class WorkflowsController : ControllerBase
{
    private readonly IWorkflowService _workflows;

    public WorkflowsController(IWorkflowService workflows) => _workflows = workflows;

    /// <summary>Lists automation rules, active ones first and in the order they run.</summary>
    [HttpGet]
    [RequiresPermission(Permissions.WorkflowRead)]
    [ProducesResponseType(typeof(PagedResult<WorkflowSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<WorkflowSummaryDto>>> Search(
        [FromQuery] WorkflowSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await _workflows.SearchAsync(request.ToQuery(), cancellationToken));
    }

    /// <summary>One rule with its conditions and actions.</summary>
    [HttpGet("{id:guid}")]
    [RequiresPermission(Permissions.WorkflowRead)]
    [ProducesResponseType(typeof(WorkflowDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _workflows.GetAsync(id, cancellationToken));

    /// <summary>
    /// The fields a rule can be written against for one module.
    /// <para>
    /// Published rather than documented, so the rule editor offers what the engine will actually
    /// accept instead of asking somebody to remember field names.
    /// </para>
    /// </summary>
    [HttpGet("fields")]
    [RequiresPermission(Permissions.WorkflowRead)]
    [ProducesResponseType(typeof(IReadOnlyList<WorkflowFieldDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<WorkflowFieldDto>> Fields([FromQuery] ServiceModule module)
        => Ok(_workflows.GetAvailableFields(module));

    /// <summary>
    /// What automation has done, newest first.
    /// <para>
    /// Includes runs that did nothing. "Why did my rule not fire" is the question people
    /// actually have, and a history of successes cannot answer it.
    /// </para>
    /// </summary>
    [HttpGet("runs")]
    [RequiresPermission(Permissions.WorkflowRead)]
    [ProducesResponseType(typeof(PagedResult<WorkflowRunDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<WorkflowRunDto>>> Runs(
        [FromQuery] WorkflowRunSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await _workflows.GetRunsAsync(request.ToQuery(), cancellationToken));
    }

    /// <summary>Creates an automation rule.</summary>
    [HttpPost]
    [RequiresPermission(Permissions.WorkflowManage)]
    [ProducesResponseType(typeof(WorkflowDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<WorkflowDetailDto>> Create(
        [FromBody] UpsertWorkflowCommand command,
        CancellationToken cancellationToken)
    {
        var created = await _workflows.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id, version = "1.0" }, created);
    }

    /// <summary>
    /// Replaces a rule's name, conditions and actions.
    /// <para>
    /// The module and trigger are fixed once the rule exists: changing them would leave the run
    /// history describing something the rule no longer is.
    /// </para>
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequiresPermission(Permissions.WorkflowManage)]
    [ProducesResponseType(typeof(WorkflowDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<WorkflowDetailDto>> Update(
        Guid id,
        [FromBody] UpsertWorkflowCommand command,
        CancellationToken cancellationToken)
        => Ok(await _workflows.UpdateAsync(id, command, cancellationToken));

    /// <summary>Switches a rule on or off. Rules are deactivated, never deleted.</summary>
    [HttpPost("{id:guid}/active")]
    [RequiresPermission(Permissions.WorkflowManage)]
    [ProducesResponseType(typeof(WorkflowDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<WorkflowDetailDto>> SetActive(
        Guid id,
        [FromBody] SetWorkflowActiveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await _workflows.SetActiveAsync(id, request.IsActive, cancellationToken));
    }
}

/// <summary>Query-string binding for the rule list.</summary>
public sealed class WorkflowSearchRequest
{
    public string? Search { get; set; }
    public ServiceModule? Module { get; set; }
    public WorkflowTrigger? Trigger { get; set; }
    public bool? IsActive { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = PagedQuery.DefaultPageSize;

    public WorkflowQuery ToQuery() => new()
    {
        Search = Search,
        Module = Module,
        Trigger = Trigger,
        IsActive = IsActive,
        Page = Page,
        PageSize = PageSize
    };
}

/// <summary>Query-string binding for the run history.</summary>
public sealed class WorkflowRunSearchRequest
{
    public Guid? WorkflowDefinitionId { get; set; }
    public Guid? RecordId { get; set; }
    public WorkflowRunStatus? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = PagedQuery.DefaultPageSize;

    public WorkflowRunQuery ToQuery() => new()
    {
        WorkflowDefinitionId = WorkflowDefinitionId,
        RecordId = RecordId,
        Status = Status,
        Page = Page,
        PageSize = PageSize
    };
}

public sealed class SetWorkflowActiveRequest
{
    public bool IsActive { get; set; }
}
