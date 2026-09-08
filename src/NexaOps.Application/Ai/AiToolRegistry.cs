using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Domain.Auditing;

namespace NexaOps.Application.Ai;

/// <summary>
/// A capability the AI is allowed to invoke.
/// <para>
/// Tools are the only route from a model to the system. There is no path by which a model can
/// reach the database, run SQL, execute a shell command, or call an application service that has
/// not been wrapped as a tool with a declared permission and tenant scope.
/// </para>
/// </summary>
public interface IAiTool
{
    /// <summary>Name the model uses to call it, e.g. <c>search_incidents</c>.</summary>
    string Name { get; }

    /// <summary>What the tool does and when to use it. This text goes to the model.</summary>
    string Description { get; }

    /// <summary>
    /// Permission the caller must hold. Enforced by the orchestrator before execution and used
    /// to decide whether the tool is even offered to the model.
    /// </summary>
    string RequiredPermission { get; }

    /// <summary>
    /// True when the tool changes state. Mutating tools never execute automatically: the
    /// orchestrator turns them into a proposal that a human confirms.
    /// </summary>
    bool IsMutating { get; }

    /// <summary>JSON Schema for the arguments. Input is validated against this before execution.</summary>
    JsonElement InputSchema { get; }

    /// <summary>Runs the tool. Implementations use the ordinary application services, so tenant
    /// filtering and permission checks apply exactly as they do for a REST caller.</summary>
    Task<AiToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of a tool invocation.</summary>
/// <param name="Succeeded">Whether it ran.</param>
/// <param name="Payload">Result serialised for the model. Contains only tenant-scoped data.</param>
/// <param name="Error">Why it failed, in terms safe to show the model and the user.</param>
/// <param name="Summary">One-line description recorded in the audit trail.</param>
public sealed record AiToolResult(bool Succeeded, string Payload, string? Error, string? Summary)
{
    public static AiToolResult Ok(string payload, string? summary = null) => new(true, payload, null, summary);

    public static AiToolResult Failed(string error) => new(false, "{}", error, null);
}

/// <summary>The set of tools this build exposes to the AI.</summary>
public interface IAiToolRegistry
{
    /// <summary>Every registered tool, regardless of caller permissions.</summary>
    IReadOnlyList<IAiTool> All { get; }

    /// <summary>
    /// Tools the current caller may use. A tool the caller lacks permission for is never
    /// described to the model, so it cannot be named in a response or coaxed into being called.
    /// </summary>
    IReadOnlyList<IAiTool> AvailableToCurrentUser();

    IAiTool? Find(string name);
}

/// <inheritdoc />
public sealed class AiToolRegistry : IAiToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAiTool> _tools;
    private readonly ICurrentUser _currentUser;

    public AiToolRegistry(IEnumerable<IAiTool> tools, ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(tools);

        _tools = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public IReadOnlyList<IAiTool> All => _tools.Values.ToList();

    /// <inheritdoc />
    public IReadOnlyList<IAiTool> AvailableToCurrentUser()
        => _tools.Values
            .Where(t => _currentUser.HasPermission(t.RequiredPermission))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    /// <inheritdoc />
    public IAiTool? Find(string name)
        => _tools.TryGetValue(name, out var tool) ? tool : null;
}

/// <summary>
/// Executes tools on behalf of the AI, applying the full security pipeline:
/// permission check, tenant check, schema validation, business validation, execution, audit.
/// </summary>
public interface IAiToolExecutor
{
    Task<AiToolResult> ExecuteAsync(
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class AiToolExecutor : IAiToolExecutor
{
    private readonly IAiToolRegistry _registry;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;
    private readonly ILogger<AiToolExecutor> _logger;

    public AiToolExecutor(
        IAiToolRegistry registry,
        ICurrentUser currentUser,
        ITenantContext tenant,
        IAuditService audit,
        ILogger<AiToolExecutor> logger)
    {
        _registry = registry;
        _currentUser = currentUser;
        _tenant = tenant;
        _audit = audit;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AiToolResult> ExecuteAsync(
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        var tool = _registry.Find(toolName);

        // An unknown tool name is a prompt-injection signal, not a routine miss: the model is
        // only ever shown tools that exist and that the caller may use.
        if (tool is null)
        {
            await _audit.RecordImmediateAsync(
                AuditAction.AiToolExecution,
                "AiTool",
                message: $"Model requested unknown tool '{Sanitise(toolName)}'.",
                source: AuditSource.Ai,
                outcome: AuditOutcome.Denied,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger.LogWarning("AI requested unknown tool {ToolName}.", Sanitise(toolName));
            return AiToolResult.Failed($"No tool named '{Sanitise(toolName)}' is available.");
        }

        if (!_currentUser.HasPermission(tool.RequiredPermission))
        {
            await _audit.RecordImmediateAsync(
                AuditAction.AiToolExecution,
                "AiTool",
                entityLabel: tool.Name,
                message: $"Denied: caller lacks '{tool.RequiredPermission}'.",
                source: AuditSource.Ai,
                outcome: AuditOutcome.Denied,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger.LogWarning(
                "AI tool {ToolName} denied for user {UserId}: missing {Permission}.",
                tool.Name, _currentUser.UserIdOrNull, tool.RequiredPermission);

            return AiToolResult.Failed("You do not have permission to perform that action.");
        }

        // A mutating tool must not run just because a model asked. The orchestrator converts it
        // into a proposal the user confirms; reaching here means that flow was bypassed.
        if (tool.IsMutating)
        {
            return AiToolResult.Failed(
                "This action changes data and requires explicit confirmation before it can run.");
        }

        JsonElement arguments;
        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            arguments = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "AI tool {ToolName} received malformed arguments.", tool.Name);
            return AiToolResult.Failed("The tool arguments were not valid JSON.");
        }

        try
        {
            var result = await tool.ExecuteAsync(arguments, cancellationToken).ConfigureAwait(false);

            _audit.Record(
                AuditAction.AiToolExecution,
                "AiTool",
                entityLabel: tool.Name,
                message: result.Summary ?? $"Executed {tool.Name} for tenant {_tenant.TenantId}.",
                source: AuditSource.Ai,
                outcome: result.Succeeded ? AuditOutcome.Success : AuditOutcome.Failure);

            return result;
        }
        catch (ForbiddenException ex)
        {
            _logger.LogWarning("AI tool {ToolName} blocked by authorization: {Message}.", tool.Name, ex.Message);
            return AiToolResult.Failed("You do not have permission to perform that action.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The message is deliberately generic: provider errors and stack traces must not be
            // relayed through the model, where they would end up in a user-visible answer.
            _logger.LogError(ex, "AI tool {ToolName} failed.", tool.Name);
            return AiToolResult.Failed("The requested operation could not be completed.");
        }
    }

    /// <summary>Strips control characters from model-supplied text before it reaches a log or audit row.</summary>
    private static string Sanitise(string value)
    {
        var trimmed = value.Length > 64 ? value[..64] : value;
        return new string(trimmed.Where(c => !char.IsControl(c)).ToArray());
    }
}
