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

    public AiController(IAiAssistantService assistant) => _assistant = assistant;

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
}
