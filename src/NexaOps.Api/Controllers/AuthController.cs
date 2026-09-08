using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NexaOps.Application.Identity;

namespace NexaOps.Api.Controllers;

/// <summary>
/// Authentication for the local sign-in mode.
/// <para>
/// Under Entra ID the browser obtains a token from Microsoft and only
/// <see cref="Profile"/> is used here. Sign-in endpoints are rate limited per IP because they
/// are what an attacker uses to guess passwords.
/// </para>
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/auth")]
[Asp.Versioning.ApiVersion("1.0")]
[Produces("application/json")]
[EnableRateLimiting("authentication")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthenticationService _auth;
    private readonly NexaOpsMetrics _metrics;

    public AuthController(IAuthenticationService auth, NexaOpsMetrics metrics)
    {
        _auth = auth;
        _metrics = metrics;
    }

    /// <summary>Exchanges credentials for an access token and a rotating refresh token.</summary>
    /// <remarks>
    /// Every failure returns the same 401 and the same message. Unknown account, wrong password
    /// and locked account are indistinguishable to the caller, so this endpoint cannot be used
    /// to discover which email addresses are registered.
    /// </remarks>
    [HttpPost("sign-in")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthenticationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthenticationResult>> SignIn(
        [FromBody] SignInRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _auth.SignInAsync(request, cancellationToken));
        }
        catch (AuthenticationFailedException ex)
        {
            _metrics.AuthenticationFailed(ex.Reason);
            throw;
        }
    }

    /// <summary>Exchanges a refresh token for a new pair, rotating the presented token.</summary>
    /// <remarks>
    /// Presenting a token that has already been rotated is treated as theft: the entire session
    /// chain is revoked and a security audit event is written.
    /// </remarks>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthenticationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthenticationResult>> Refresh(
        [FromBody] RefreshRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await _auth.RefreshAsync(request.RefreshToken, cancellationToken));
    }

    /// <summary>Revokes the presented refresh token and everything else in its session.</summary>
    [HttpPost("sign-out")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SignOutSession(
        [FromBody] RefreshRequest request,
        CancellationToken cancellationToken)
    {
        await _auth.SignOutAsync(request?.RefreshToken, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// The signed-in user, with their tenant, effective permissions and group memberships.
    /// The SPA calls this on load to build its navigation and hide actions the user cannot take.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserProfileDto>> Profile(CancellationToken cancellationToken)
        => Ok(await _auth.GetProfileAsync(cancellationToken));

    /// <summary>Changes the caller's own password. Every other active session is revoked.</summary>
    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _auth.ChangePasswordAsync(request, cancellationToken);
        return NoContent();
    }
}

/// <summary>Carries an opaque refresh token.</summary>
public sealed class RefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}
