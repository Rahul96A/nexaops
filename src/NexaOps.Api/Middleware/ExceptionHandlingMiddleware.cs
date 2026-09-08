using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NexaOps.Api.Identity;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Ai;
using NexaOps.Application.Common;
using NexaOps.Application.Identity;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Common;

namespace NexaOps.Api.Middleware;

/// <summary>
/// Turns every exception into an RFC 9457 Problem Details response.
/// <para>
/// Two rules govern what a client is told. First, the response carries a stable machine-readable
/// <c>type</c> so the UI can react to a specific failure rather than parsing prose. Second, no
/// internal detail - stack trace, SQL text, provider message - ever crosses the boundary outside
/// Development; it goes to the log and to Application Insights, keyed by correlation id.
/// </para>
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private const string ProblemBaseUri = "https://docs.nexaops.io/problems/";

    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        IHostEnvironment environment,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _environment = environment;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IAuditService audit)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await HandleAsync(context, exception, audit).ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception, IAuditService audit)
    {
        if (context.Response.HasStarted)
        {
            // Too late to change the response; make sure the failure is still visible.
            _logger.LogError(exception, "An exception occurred after the response had started.");
            return;
        }

        var correlationId = context.Items[HttpCorrelationContext.HeaderName] as string
                            ?? context.TraceIdentifier;

        var (status, type, title, detail, extensions) = Map(exception);

        // Security-relevant outcomes are audited even though the operation failed - especially
        // because it failed.
        if (exception is ForbiddenException forbidden)
        {
            await SafeAuditAsync(
                audit,
                AuditAction.AccessDenied,
                $"Denied '{forbidden.Permission}' on {context.Request.Method} {context.Request.Path}.",
                AuditOutcome.Denied,
                context.RequestAborted).ConfigureAwait(false);
        }
        else if (exception is TenantIsolationViolationException isolation)
        {
            await SafeAuditAsync(
                audit,
                AuditAction.SecurityEvent,
                $"Tenant isolation violation on {isolation.EntityType}.",
                AuditOutcome.Denied,
                context.RequestAborted).ConfigureAwait(false);
        }

        if (status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                exception,
                "Unhandled exception on {Method} {Path}. Correlation {CorrelationId}.",
                context.Request.Method, context.Request.Path, correlationId);
        }
        else
        {
            _logger.LogInformation(
                "Request failed with {Status} on {Method} {Path}: {Message}. Correlation {CorrelationId}.",
                status, context.Request.Method, context.Request.Path, exception.Message, correlationId);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Type = ProblemBaseUri + type,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };

        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["code"] = type;

        foreach (var (key, value) in extensions)
        {
            problem.Extensions[key] = value;
        }

        if (_environment.IsDevelopment() && status >= StatusCodes.Status500InternalServerError)
        {
            problem.Extensions["exception"] = exception.ToString();
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(problem, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>Maps an exception onto a status code and a stable problem type.</summary>
    private (int Status, string Type, string Title, string Detail, Dictionary<string, object?> Extensions)
        Map(Exception exception)
    {
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal);

        switch (exception)
        {
            case ValidationException validation:
                extensions["errors"] = validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => JsonNamingPolicy.CamelCase.ConvertName(g.Key),
                        g => g.Select(e => e.ErrorMessage).ToArray());

                return (StatusCodes.Status400BadRequest, "validation_failed",
                    "The request could not be validated.",
                    "One or more fields are invalid. See the errors property.", extensions);

            case EntityNotFoundException notFound:
                // A record in another tenant, or one the caller may not see, is reported as
                // absent rather than forbidden - existence itself can be information.
                return (StatusCodes.Status404NotFound, "not_found",
                    "The requested record was not found.",
                    $"No {notFound.EntityType} matches the supplied identifier.", extensions);

            case ForbiddenException:
                // The specific missing permission is logged and audited, not returned; telling a
                // caller exactly which permission to acquire is an aid to privilege escalation.
                return (StatusCodes.Status403Forbidden, "forbidden",
                    "You do not have permission to perform this action.",
                    "Contact your NexaOps administrator if you believe this is incorrect.", extensions);

            case TenantIsolationViolationException:
                return (StatusCodes.Status403Forbidden, "forbidden",
                    "You do not have permission to perform this action.",
                    "The requested operation was refused.", extensions);

            case ConflictException conflict:
                return (StatusCodes.Status409Conflict, conflict.Code,
                    "The request conflicts with the current state of the record.",
                    conflict.Message, extensions);

            case TenantSelectionRequiredException selection:
                extensions["tenants"] = selection.Choices;
                return (StatusCodes.Status409Conflict, "tenant_selection_required",
                    "Choose an organization to continue.",
                    selection.Message, extensions);

            case AuthenticationFailedException:
                // Deliberately uniform: unknown account, wrong password and locked account are
                // indistinguishable to a caller, so the endpoint cannot enumerate users.
                return (StatusCodes.Status401Unauthorized, "authentication_failed",
                    "Sign-in failed.",
                    "The email address or password is incorrect.", extensions);

            case AiNotConfiguredException:
                return (StatusCodes.Status503ServiceUnavailable, "ai_not_configured",
                    "AI features are not available in this environment.",
                    "No AI provider is configured. Configure Azure OpenAI to enable AI features.",
                    extensions);

            case DomainException domain:
                return (StatusCodes.Status422UnprocessableEntity, domain.Code,
                    "The operation is not valid for this record.",
                    domain.Message, extensions);

            case UnauthorizedAccessException:
                return (StatusCodes.Status403Forbidden, "forbidden",
                    "You do not have permission to perform this action.",
                    "The requested operation was refused.", extensions);

            case OperationCanceledException:
                return (StatusCodesExtra.ClientClosedRequest, "request_cancelled",
                    "The request was cancelled.",
                    "The client closed the connection before the request completed.", extensions);

            case BadHttpRequestException bad:
                return (StatusCodes.Status400BadRequest, "malformed_request",
                    "The request could not be read.",
                    bad.Message, extensions);

            default:
                return (StatusCodes.Status500InternalServerError, "internal_error",
                    "An unexpected error occurred.",
                    "The error has been logged. Quote the correlation id when contacting support.",
                    extensions);
        }
    }

    /// <summary>
    /// Writes a security audit row without letting an audit failure mask the original error.
    /// </summary>
    private async Task SafeAuditAsync(
        IAuditService audit,
        AuditAction action,
        string message,
        AuditOutcome outcome,
        CancellationToken cancellationToken)
    {
        try
        {
            await audit.RecordImmediateAsync(
                action,
                "HttpRequest",
                message: message,
                outcome: outcome,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to write a security audit event.");
        }
    }
}

/// <summary>Status codes ASP.NET Core does not define as constants.</summary>
internal static class StatusCodesExtra
{
    /// <summary>nginx convention for "client closed the request".</summary>
    public const int ClientClosedRequest = 499;
}
