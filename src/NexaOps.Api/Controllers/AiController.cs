using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NexaOps.Api.Authorization;
using NexaOps.Application.Ai;
using NexaOps.Application.Security;

namespace NexaOps.Api.Controllers;

/// <summary>
/// The AI assistant.
/// <para>
/// The browser never talks to an AI provider and never holds a key. Requests arrive here, the
/// orchestrator retrieves data through permission-checked tools, and the answer is grounded in
/// what those tools actually returned.
/// </para>
/// <para>
/// When no provider is configured these endpoints return 503 with problem type
/// <c>ai_not_configured</c>. NexaOps never substitutes a fabricated answer.
/// </para>
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/ai")]
[Asp.Versioning.ApiVersion("1.0")]
[Produces("application/json")]
public sealed class AiController : ControllerBase
{
    private readonly IAiAssistantService _assistant;
    private readonly IVirtualAgentService _agent;

    public AiController(IAiAssistantService assistant, IVirtualAgentService agent)
    {
        _assistant = assistant;
        _agent = agent;
    }

    /// <summary>
    /// Whether AI is available in this environment and which tools the caller may use.
    /// The UI calls this to decide whether to show the assistant at all, rather than offering a
    /// button that fails.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(AiStatusDto), StatusCodes.Status200OK)]
    public ActionResult<AiStatusDto> Status() => Ok(_assistant.GetStatus());

    /// <summary>Asks the assistant a question grounded in the caller's own tenant data.</summary>
    [HttpPost("ask")]
    [RequiresPermission(Permissions.AiAssistantUse)]
    [EnableRateLimiting("ai")]
    [ProducesResponseType(typeof(AiAnswerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AiAnswerDto>> Ask(
        [FromBody] AiAskRequest request,
        CancellationToken cancellationToken)
        => Ok(await _assistant.AskAsync(request, cancellationToken));

    /// <summary>
    /// The employee-facing virtual agent.
    /// <para>
    /// Answers from published knowledge and the caller's own records, and where nothing helps,
    /// proposes raising a ticket. The proposal is a suggestion in the response — nothing is
    /// created here.
    /// </para>
    /// </summary>
    [HttpPost("agent/chat")]
    [RequiresPermission(Permissions.AiAgentUse)]
    [EnableRateLimiting("ai")]
    [ProducesResponseType(typeof(VirtualAgentReplyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<VirtualAgentReplyDto>> Chat(
        [FromBody] VirtualAgentRequest request,
        CancellationToken cancellationToken)
        => Ok(await _agent.ChatAsync(request, cancellationToken));

    /// <summary>
    /// Acts on a proposal the person has confirmed.
    /// <para>
    /// Held behind its own permission, and deliberately not dependent on an AI provider being
    /// configured: this endpoint calls the ordinary incident service as the signed-in user. The
    /// model suggested the wording; the person decided, and the record is theirs.
    /// </para>
    /// <para>
    /// The confirmation carries the fields as shown on screen rather than a reference to a
    /// stored proposal, so somebody who edited the title gets the title they edited and there is
    /// no server-side draft for a stale one to be resurrected from.
    /// </para>
    /// </summary>
    [HttpPost("agent/confirm")]
    [RequiresPermission(Permissions.AiActionConfirm)]
    [ProducesResponseType(typeof(VirtualAgentActionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<VirtualAgentActionResultDto>> Confirm(
        [FromBody] ConfirmVirtualAgentActionCommand command,
        CancellationToken cancellationToken)
        => Ok(await _agent.ConfirmAsync(command, cancellationToken));
}
