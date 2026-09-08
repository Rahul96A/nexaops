using Microsoft.AspNetCore.Mvc;
using NexaOps.Api.Authorization;
using NexaOps.Application.Cmdb;
using NexaOps.Application.Common;
using NexaOps.Application.Security;
using NexaOps.Domain.Cmdb;

namespace NexaOps.Api.Controllers;

/// <summary>
/// The configuration management database.
/// <para>
/// Exists to answer the two questions an outage forces: what does this thing support, and what
/// does it depend on. The record endpoint returns both, walked from the dependency graph.
/// </para>
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/cmdb")]
[Asp.Versioning.ApiVersion("1.0")]
[Produces("application/json")]
public sealed class CmdbController : ControllerBase
{
    private readonly ICmdbService _cmdb;

    public CmdbController(ICmdbService cmdb) => _cmdb = cmdb;

    /// <summary>Searches configuration items by name, number, description or serial number.</summary>
    [HttpGet("items")]
    [RequiresPermission(Permissions.CmdbRead)]
    [ProducesResponseType(typeof(PagedResult<CiSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CiSummaryDto>>> Search(
        [FromQuery] CiSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await _cmdb.SearchAsync(request.ToQuery(), cancellationToken));
    }

    /// <summary>
    /// Counters for the CMDB view, including how many live items are out of support and how many
    /// have nobody accountable for them.
    /// </summary>
    [HttpGet("summary")]
    [RequiresPermission(Permissions.CmdbRead)]
    [ProducesResponseType(typeof(CmdbSummaryCountsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CmdbSummaryCountsDto>> Summary(CancellationToken cancellationToken)
        => Ok(await _cmdb.GetSummaryAsync(cancellationToken));

    /// <summary>
    /// One configuration item with its impact and dependency analysis, nearest first.
    /// </summary>
    [HttpGet("items/{id:guid}")]
    [RequiresPermission(Permissions.CmdbRead)]
    [ProducesResponseType(typeof(CiDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CiDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _cmdb.GetAsync(id, cancellationToken));

    /// <summary>Adds a configuration item.</summary>
    [HttpPost("items")]
    [RequiresPermission(Permissions.CmdbCreate)]
    [ProducesResponseType(typeof(CiDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CiDetailDto>> Create(
        [FromBody] UpsertCiCommand command,
        CancellationToken cancellationToken)
    {
        var created = await _cmdb.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id, version = "1.0" }, created);
    }

    /// <summary>
    /// Edits a configuration item.
    /// </summary>
    /// <remarks>
    /// Setting the status to Retired or Disposed needs <c>cmdb.retire</c>, because it removes the
    /// item from everyone else's impact analysis — a bigger act than correcting a serial number.
    /// </remarks>
    [HttpPut("items/{id:guid}")]
    [RequiresPermission(Permissions.CmdbRead)]
    [ProducesResponseType(typeof(CiDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CiDetailDto>> Update(
        Guid id,
        [FromBody] UpsertCiCommand command,
        CancellationToken cancellationToken)
        => Ok(await _cmdb.UpdateAsync(id, command, cancellationToken));

    /// <summary>Adds a dependency edge from this item to another. Idempotent.</summary>
    [HttpPost("items/{id:guid}/relationships")]
    [RequiresPermission(Permissions.CmdbManageRelationships)]
    [ProducesResponseType(typeof(CiDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CiDetailDto>> AddRelationship(
        Guid id,
        [FromBody] AddRelationshipCommand command,
        CancellationToken cancellationToken)
        => Ok(await _cmdb.AddRelationshipAsync(id, command, cancellationToken));

    /// <summary>Removes a dependency edge.</summary>
    [HttpDelete("items/{id:guid}/relationships/{targetId:guid}")]
    [RequiresPermission(Permissions.CmdbManageRelationships)]
    [ProducesResponseType(typeof(CiDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CiDetailDto>> RemoveRelationship(
        Guid id,
        Guid targetId,
        [FromQuery] CiRelationshipType type = CiRelationshipType.DependsOn,
        CancellationToken cancellationToken = default)
        => Ok(await _cmdb.RemoveRelationshipAsync(id, targetId, type, cancellationToken));
}

/// <summary>Query-string binding for CMDB search.</summary>
public sealed class CiSearchRequest
{
    public string? Search { get; set; }
    public string? Type { get; set; }
    public string? Status { get; set; }
    public string? Criticality { get; set; }
    public string? Environment { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string? Scope { get; set; }
    public string SortBy { get; set; } = "name";
    public bool SortDescending { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;

    public CiQuery ToQuery() => new()
    {
        Search = Search,
        Types = ParseList<CiType>(Type),
        Statuses = ParseList<CiStatus>(Status),
        Criticalities = ParseList<CiCriticality>(Criticality),
        Environment = Environment,
        OwnerUserId = OwnerUserId,
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
