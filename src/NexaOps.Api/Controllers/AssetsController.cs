using Microsoft.AspNetCore.Mvc;
using NexaOps.Api.Authorization;
using NexaOps.Application.Assets;
using NexaOps.Application.Common;
using NexaOps.Application.Security;
using NexaOps.Domain.Assets;

namespace NexaOps.Api.Controllers;

/// <summary>
/// The asset register: what the organisation owns, who holds it, and what it cost.
/// <para>
/// Distinct from the CMDB by design. A configuration item answers what a thing supports; an asset
/// answers who has it and when it needs replacing. The same laptop is often both, and the two are
/// linked rather than merged.
/// </para>
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/assets")]
[Asp.Versioning.ApiVersion("1.0")]
[Produces("application/json")]
public sealed class AssetsController : ControllerBase
{
    private readonly IAssetService _assets;

    public AssetsController(IAssetService assets) => _assets = assets;

    /// <summary>Searches assets by tag, name, number or serial number.</summary>
    [HttpGet]
    [RequiresPermission(Permissions.AssetRead)]
    [ProducesResponseType(typeof(PagedResult<AssetSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AssetSummaryDto>>> Search(
        [FromQuery] AssetSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await _assets.SearchAsync(request.ToQuery(), cancellationToken));
    }

    /// <summary>
    /// Counters for the asset view, including the licence compliance position and an indicative
    /// exposure figure for over-deployment.
    /// </summary>
    [HttpGet("summary")]
    [RequiresPermission(Permissions.AssetRead)]
    [ProducesResponseType(typeof(AssetSummaryCountsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AssetSummaryCountsDto>> Summary(CancellationToken cancellationToken)
        => Ok(await _assets.GetSummaryAsync(cancellationToken));

    /// <summary>One asset with its full custody history, newest first.</summary>
    [HttpGet("{id:guid}")]
    [RequiresPermission(Permissions.AssetRead)]
    [ProducesResponseType(typeof(AssetDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssetDetailDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _assets.GetAsync(id, cancellationToken));

    /// <summary>Adds an asset to the register.</summary>
    [HttpPost]
    [RequiresPermission(Permissions.AssetCreate)]
    [ProducesResponseType(typeof(AssetDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AssetDetailDto>> Create(
        [FromBody] UpsertAssetCommand command,
        CancellationToken cancellationToken)
    {
        var created = await _assets.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id, version = "1.0" }, created);
    }

    /// <summary>Edits an asset's details.</summary>
    /// <remarks>Disposal has its own action, so custody is closed and the date recorded.</remarks>
    [HttpPut("{id:guid}")]
    [RequiresPermission(Permissions.AssetUpdate)]
    [ProducesResponseType(typeof(AssetDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AssetDetailDto>> Update(
        Guid id,
        [FromBody] UpsertAssetCommand command,
        CancellationToken cancellationToken)
        => Ok(await _assets.UpdateAsync(id, command, cancellationToken));

    /// <summary>Issues the asset to somebody, opening a custody record.</summary>
    [HttpPost("{id:guid}/assign")]
    [RequiresPermission(Permissions.AssetAssign)]
    [ProducesResponseType(typeof(AssetDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AssetDetailDto>> Assign(
        Guid id,
        [FromBody] AssignAssetCommand command,
        CancellationToken cancellationToken)
        => Ok(await _assets.AssignAsync(id, command, cancellationToken));

    /// <summary>Takes the asset back, closing the custody record.</summary>
    [HttpPost("{id:guid}/return")]
    [RequiresPermission(Permissions.AssetAssign)]
    [ProducesResponseType(typeof(AssetDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AssetDetailDto>> Return(
        Guid id,
        [FromBody] ReturnAssetCommand command,
        CancellationToken cancellationToken)
        => Ok(await _assets.ReturnAsync(id, command, cancellationToken));

    /// <summary>Disposes of or writes off an asset.</summary>
    /// <remarks>
    /// Closes any open custody first, so nobody stays accountable for a thing that no longer
    /// exists. Narrowly permissioned, because it removes something a finance audit expects to be
    /// able to count.
    /// </remarks>
    [HttpPost("{id:guid}/dispose")]
    [RequiresPermission(Permissions.AssetDispose)]
    [ProducesResponseType(typeof(AssetDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AssetDetailDto>> DisposeOf(
        Guid id,
        [FromBody] DisposeAssetCommand command,
        CancellationToken cancellationToken)
        => Ok(await _assets.DisposeAsync(id, command, cancellationToken));

    /// <summary>Software licences and the compliance position against each.</summary>
    [HttpGet("licences")]
    [RequiresPermission(Permissions.LicenceRead)]
    [ProducesResponseType(typeof(IReadOnlyList<LicenceSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LicenceSummaryDto>>> Licences(
        CancellationToken cancellationToken)
        => Ok(await _assets.GetLicencesAsync(cancellationToken));

    /// <summary>Records a licence agreement.</summary>
    [HttpPost("licences")]
    [RequiresPermission(Permissions.LicenceManage)]
    [ProducesResponseType(typeof(LicenceSummaryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LicenceSummaryDto>> CreateLicence(
        [FromBody] UpsertLicenceCommand command,
        CancellationToken cancellationToken)
    {
        var created = await _assets.CreateLicenceAsync(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>
    /// Updates a licence agreement, including the deployment count that drives compliance.
    /// </summary>
    [HttpPut("licences/{id:guid}")]
    [RequiresPermission(Permissions.LicenceManage)]
    [ProducesResponseType(typeof(LicenceSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LicenceSummaryDto>> UpdateLicence(
        Guid id,
        [FromBody] UpsertLicenceCommand command,
        CancellationToken cancellationToken)
        => Ok(await _assets.UpdateLicenceAsync(id, command, cancellationToken));
}

/// <summary>Query-string binding for asset search.</summary>
public sealed class AssetSearchRequest
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? Kind { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public string? Scope { get; set; }
    public string SortBy { get; set; } = "assetTag";
    public bool SortDescending { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;

    public AssetQuery ToQuery() => new()
    {
        Search = Search,
        Statuses = ParseList<AssetStatus>(Status),
        Kinds = ParseList<AssetKind>(Kind),
        AssignedToUserId = AssignedToUserId,
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
