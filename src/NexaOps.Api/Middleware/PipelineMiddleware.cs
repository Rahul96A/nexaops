using System.Diagnostics;
using NexaOps.Api.Identity;
using NexaOps.Application.Abstractions;
using NexaOps.Infrastructure.Identity;
using Serilog.Context;

namespace NexaOps.Api.Middleware;

/// <summary>
/// Assigns a correlation id to every request and echoes it back, so a customer can quote one
/// identifier that ties together the API log, the distributed trace, and the audit trail.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var incoming = context.Request.Headers[HttpCorrelationContext.HeaderName].ToString();

        // A client-supplied value is accepted for trace continuity but never trusted for
        // anything security-relevant, and is length-capped so it cannot bloat a log line.
        var correlationId = string.IsNullOrWhiteSpace(incoming)
            ? Activity.Current?.TraceId.ToString() ?? Guid.CreateVersion7().ToString("N")
            : Sanitise(incoming);

        context.Items[HttpCorrelationContext.HeaderName] = correlationId;
        context.Response.Headers[HttpCorrelationContext.HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context).ConfigureAwait(false);
        }
    }

    private static string Sanitise(string value)
    {
        var trimmed = value.Length > 64 ? value[..64] : value;
        return new string(trimmed.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray());
    }
}

/// <summary>
/// Establishes the ambient tenant scope from the authenticated principal.
/// <para>
/// This is the single place a tenant is ever set for a request, and it reads only the
/// <c>nexaops:tid</c> claim on a signature-validated token. An unauthenticated request gets no
/// tenant scope at all, which makes every tenant-filtered query return nothing.
/// </para>
/// </summary>
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContextSetter tenantSetter)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantSetter);

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var tenantClaim = context.User.FindFirst(NexaOpsClaims.TenantId)?.Value;

        if (!Guid.TryParse(tenantClaim, out var tenantId) || tenantId == Guid.Empty)
        {
            // An authenticated token with no usable tenant claim is malformed. Refusing here is
            // safer than continuing with no scope and letting queries quietly return nothing.
            _logger.LogWarning("Authenticated request carried no valid tenant claim.");

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("The security token does not identify a tenant.")
                .ConfigureAwait(false);
            return;
        }

        var tenantCode = context.User.FindFirst(NexaOpsClaims.TenantCode)?.Value ?? string.Empty;
        var timeZoneId = context.User.FindFirst(NexaOpsClaims.TenantTimeZone)?.Value
                         ?? "India Standard Time";

        using (tenantSetter.BeginScope(tenantId, tenantCode, timeZoneId))
        using (LogContext.PushProperty("TenantId", tenantId))
        using (LogContext.PushProperty("UserId", context.User.FindFirst(NexaOpsClaims.UserId)?.Value))
        {
            await _next(context).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Adds the response headers that keep a browser from being turned against the user.
/// <para>
/// The API returns JSON, never HTML, so the content security policy is maximally restrictive:
/// nothing is allowed to load or execute. The SPA is served separately with its own policy.
/// </para>
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = context.Response.Headers;

        // Refuse to let a browser guess a content type; JSON must be treated as JSON.
        headers["X-Content-Type-Options"] = "nosniff";

        // An API has no reason to be framed.
        headers["X-Frame-Options"] = "DENY";
        headers["Content-Security-Policy"] =
            "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Resource-Policy"] = "same-site";

        // No API endpoint needs a camera, a microphone or a location.
        headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), microphone=(), payment=()";

        // Never let a proxy or browser cache an authenticated API response.
        if (!headers.ContainsKey("Cache-Control"))
        {
            headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        }

        // Announce the server as little as possible.
        headers.Remove("Server");
        headers.Remove("X-Powered-By");

        await _next(context).ConfigureAwait(false);
    }
}
