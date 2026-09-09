using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NexaOps.Api.Authorization;
using NexaOps.Api.Identity;
using NexaOps.Application.Common;
using NexaOps.Application.Integration;
using NexaOps.Application.Security;
using NexaOps.Domain.Integration;

namespace NexaOps.Api.Controllers;

/// <summary>
/// What arrives from outside the product.
/// <para>
/// The ingestion endpoint authenticates with an integration key rather than a person's token,
/// and everything downstream treats that key as an ordinary authenticated caller — same tenant
/// resolution, same permission checks, same query filters. A machine caller is not a special
/// case with its own rules; it is a caller whose credential happens to be a key.
/// </para>
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/integration")]
[Asp.Versioning.ApiVersion("1.0")]
[Produces("application/json")]
public sealed class IntegrationController : ControllerBase
{
    private readonly IInboundEmailService _email;
    private readonly IIntegrationKeyService _keys;

    public IntegrationController(IInboundEmailService email, IIntegrationKeyService keys)
    {
        _email = email;
        _keys = keys;
    }

    /// <summary>
    /// Delivers one parsed email into the service desk.
    /// <para>
    /// Idempotent on the Message-ID: a provider retrying a delivery it already made gets the
    /// original outcome back and nothing is created twice. Called by a mail provider's inbound
    /// parse webhook, which is why it authenticates with a key rather than a session.
    /// </para>
    /// </summary>
    [HttpPost("email")]
    [Authorize(AuthenticationSchemes = IntegrationKeyAuthenticationHandler.SchemeName)]
    [RequiresPermission(Permissions.IncidentCreate)]
    [EnableRateLimiting("integration")]
    [ProducesResponseType(typeof(InboundEmailResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<InboundEmailResultDto>> ReceiveEmail(
        [FromBody] InboundEmailCommand command,
        CancellationToken cancellationToken)
    {
        // The key's scope, checked separately from its service account's permissions. A key
        // issued for email cannot be pointed elsewhere even if its account could go there.
        if (HttpContext.Items["IntegrationKeyScope"] is not IntegrationScope.InboundEmail)
        {
            return Forbid();
        }

        return Ok(await _email.ReceiveAsync(command, cancellationToken));
    }

    /// <summary>
    /// What has arrived and what became of it, including the messages that were ignored.
    /// <para>
    /// "I emailed the service desk and nothing happened" is the question this module gets asked,
    /// and it cannot be answered from a record of successes.
    /// </para>
    /// </summary>
    [HttpGet("email/messages")]
    [RequiresPermission(Permissions.IntegrationManage)]
    [ProducesResponseType(typeof(PagedResult<InboundMessageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<InboundMessageDto>>> Messages(
        [FromQuery] InboundMessageSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await _email.GetMessagesAsync(request.ToQuery(), cancellationToken));
    }

    // -----------------------------------------------------------------
    // Keys
    // -----------------------------------------------------------------

    /// <summary>The tenant's machine credentials. Never the keys themselves — only their prefixes.</summary>
    [HttpGet("keys")]
    [RequiresPermission(Permissions.IntegrationManage)]
    [ProducesResponseType(typeof(IReadOnlyList<IntegrationKeyDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<IntegrationKeyDto>>> Keys(CancellationToken cancellationToken)
        => Ok(await _keys.GetKeysAsync(cancellationToken));

    /// <summary>
    /// Issues a key.
    /// <para>
    /// The secret is in this response and nowhere else. It is stored as a hash and cannot be
    /// recovered, so a key nobody wrote down has to be revoked and replaced — which is the
    /// correct cost of a credential having exactly one owner.
    /// </para>
    /// </summary>
    [HttpPost("keys")]
    [RequiresPermission(Permissions.IntegrationManage)]
    [ProducesResponseType(typeof(IssuedIntegrationKeyDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IssuedIntegrationKeyDto>> IssueKey(
        [FromBody] IssueIntegrationKeyCommand command,
        CancellationToken cancellationToken)
    {
        var issued = await _keys.IssueAsync(command, cancellationToken);
        return Created($"/api/v1/integration/keys/{issued.Details.Id}", issued);
    }

    /// <summary>
    /// Revokes a key immediately.
    /// <para>
    /// Revoked, never deleted: the key's identifier appears in the audit entries for everything
    /// it did, and removing the row would leave those pointing at nothing.
    /// </para>
    /// </summary>
    [HttpDelete("keys/{id:guid}")]
    [RequiresPermission(Permissions.IntegrationManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeKey(Guid id, CancellationToken cancellationToken)
    {
        await _keys.RevokeAsync(id, cancellationToken);
        return NoContent();
    }
}

/// <summary>Query-string binding for the ingestion history.</summary>
public sealed class InboundMessageSearchRequest
{
    public InboundMessageStatus? Status { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = PagedQuery.DefaultPageSize;

    public InboundMessageQuery ToQuery() => new()
    {
        Status = Status,
        Search = Search,
        Page = Page,
        PageSize = PageSize
    };
}
