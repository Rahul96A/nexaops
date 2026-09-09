using Microsoft.AspNetCore.Mvc;
using NexaOps.Api.Authorization;
using NexaOps.Application.Common;
using NexaOps.Application.Platform;
using NexaOps.Application.Security;
using NexaOps.Domain.Identity;

namespace NexaOps.Api.Controllers;

/// <summary>
/// Platform administration: onboarding and managing the customers of NexaOps itself.
/// <para>
/// This is the one controller whose operations cross tenant boundaries, and it is the only one
/// gated by <c>platform.*</c> permissions. Those permissions live exclusively on a
/// platform-scoped role, and a tenant administrator cannot assign a platform-scoped role to
/// anybody — so holding every permission inside a tenant still gets a caller nothing here.
/// </para>
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/platform")]
[Asp.Versioning.ApiVersion("1.0")]
[Produces("application/json")]
public sealed class PlatformController : ControllerBase
{
    private readonly ITenantOnboardingService _tenants;

    public PlatformController(ITenantOnboardingService tenants) => _tenants = tenants;

    /// <summary>Every customer on the platform, newest first.</summary>
    [HttpGet("tenants")]
    [RequiresPermission(Permissions.PlatformTenantRead)]
    [ProducesResponseType(typeof(PagedResult<TenantSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<TenantSummaryDto>>> Tenants(
        [FromQuery] TenantSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await _tenants.SearchAsync(request.ToQuery(), cancellationToken));
    }

    /// <summary>One customer in full.</summary>
    [HttpGet("tenants/{id:guid}")]
    [RequiresPermission(Permissions.PlatformTenantRead)]
    [ProducesResponseType(typeof(TenantDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TenantDetailDto>> GetTenant(Guid id, CancellationToken cancellationToken)
        => Ok(await _tenants.GetAsync(id, cancellationToken));

    /// <summary>
    /// Onboards a customer: creates the tenant, provisions its baseline configuration, and
    /// creates the first administrator who can then run it from inside the product.
    /// <para>
    /// The response carries that administrator's one-time password. It is returned here and
    /// nowhere else — not in the audit trail, not in the log, and not retrievable afterwards.
    /// The account is flagged to require a password change at first sign-in, so the operator's
    /// copy stops working as soon as the customer uses it.
    /// </para>
    /// </summary>
    [HttpPost("tenants")]
    [RequiresPermission(Permissions.PlatformTenantManage)]
    [ProducesResponseType(typeof(TenantOnboardingResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TenantOnboardingResult>> Onboard(
        [FromBody] CreateTenantCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _tenants.OnboardAsync(command, cancellationToken);

        return CreatedAtAction(
            nameof(GetTenant),
            new { id = result.Tenant.Id, version = "1.0" },
            result);
    }

    /// <summary>
    /// Changes a customer's settings. The tenant code is immutable and is not accepted here: it
    /// is embedded in sign-in, in support conversations and in whatever anyone has bookmarked.
    /// </summary>
    [HttpPut("tenants/{id:guid}")]
    [RequiresPermission(Permissions.PlatformTenantManage)]
    [ProducesResponseType(typeof(TenantDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TenantDetailDto>> UpdateTenant(
        Guid id,
        [FromBody] UpdateTenantCommand command,
        CancellationToken cancellationToken)
        => Ok(await _tenants.UpdateAsync(id, command, cancellationToken));

    /// <summary>
    /// Suspends, reactivates or closes a customer.
    /// <para>
    /// Suspension is enforced, not cosmetic: a suspended tenant admits no sign-in and no token
    /// refresh, and its live sessions are revoked. A reason is required, and it goes into the
    /// customer's own audit trail so that "why can nobody sign in" has a written answer.
    /// </para>
    /// </summary>
    [HttpPost("tenants/{id:guid}/status")]
    [RequiresPermission(Permissions.PlatformTenantManage)]
    [ProducesResponseType(typeof(TenantDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TenantDetailDto>> SetTenantStatus(
        Guid id,
        [FromBody] SetTenantStatusCommand command,
        CancellationToken cancellationToken)
        => Ok(await _tenants.SetStatusAsync(id, command, cancellationToken));
}

/// <summary>Query-string binding for the tenant list.</summary>
public sealed class TenantSearchRequest
{
    public TenantStatus? Status { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = PagedQuery.DefaultPageSize;

    public TenantQuery ToQuery() => new()
    {
        Status = Status,
        Search = Search,
        Page = Page,
        PageSize = PageSize
    };
}
