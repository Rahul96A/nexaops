using System.Text.Json;

namespace NexaOps.Application.Ai;

/// <summary>
/// The AI provider port. Implemented by the Azure OpenAI adapter in the infrastructure layer.
/// <para>
/// The browser never reaches a provider directly and never holds a key. Everything crosses this
/// interface, which is where permission, tenant scope and audit are applied.
/// </para>
/// </summary>
public interface IAiCompletionService
{
    /// <summary>
    /// Whether a provider is configured in this environment. When false, AI endpoints return
    /// <c>503 ai_not_configured</c>. NexaOps never substitutes a canned answer for a real one:
    /// an environment without credentials shows AI as unavailable.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>Provider and deployment names, surfaced in the admin UI and in health checks.</summary>
    AiProviderInfo ProviderInfo { get; }

    /// <summary>
    /// Sends a conversation and returns either a message or a request to call one or more tools.
    /// Throws <see cref="AiNotConfiguredException"/> when <see cref="IsConfigured"/> is false.
    /// </summary>
    Task<AiCompletionResult> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Embeds text for semantic search. Throws when embeddings are not configured.</summary>
    Task<IReadOnlyList<float>> EmbedAsync(string text, CancellationToken cancellationToken = default);
}

/// <param name="Provider">e.g. <c>AzureOpenAI</c>, or <c>None</c> when unconfigured.</param>
/// <param name="ChatDeployment">Chat model deployment name, null when unconfigured.</param>
/// <param name="EmbeddingDeployment">Embedding deployment name, null when unconfigured.</param>
/// <param name="EmbeddingsAvailable">Whether semantic search is usable in this environment.</param>
public sealed record AiProviderInfo(
    string Provider,
    string? ChatDeployment,
    string? EmbeddingDeployment,
    bool EmbeddingsAvailable)
{
    public static readonly AiProviderInfo NotConfigured = new("None", null, null, false);
}

/// <summary>One turn in a conversation sent to the model.</summary>
/// <param name="Role">One of <c>system</c>, <c>user</c>, <c>assistant</c>, <c>tool</c>.</param>
/// <param name="Content">Message text.</param>
/// <param name="ToolCallId">Correlates a tool result back to the call that asked for it.</param>
/// <param name="ToolName">Name of the tool this message reports the result of.</param>
public sealed record AiMessage(string Role, string Content, string? ToolCallId = null, string? ToolName = null)
{
    public static AiMessage System(string content) => new("system", content);
    public static AiMessage User(string content) => new("user", content);
    public static AiMessage Assistant(string content) => new("assistant", content);
    public static AiMessage ToolResult(string toolCallId, string toolName, string content)
        => new("tool", content, toolCallId, toolName);
}

/// <summary>A request to the model, including the tools it is permitted to call this turn.</summary>
public sealed class AiCompletionRequest
{
    public required IReadOnlyList<AiMessage> Messages { get; init; }

    /// <summary>
    /// Tool descriptors the model may call. The orchestrator only ever puts tools here that the
    /// caller already holds the permission for, so the model cannot be talked into naming a tool
    /// it was never offered.
    /// </summary>
    public IReadOnlyList<AiToolDescriptor> Tools { get; init; } = [];

    /// <summary>Low by default: grounded ITSM answers should be reproducible, not creative.</summary>
    public float Temperature { get; init; } = 0.1f;

    public int MaxOutputTokens { get; init; } = 1200;
}

/// <summary>What the model returned: either prose, or a request to call tools.</summary>
/// <param name="Content">Assistant text, null when the model asked for tools instead.</param>
/// <param name="ToolCalls">Tools the model wants invoked.</param>
/// <param name="FinishReason">Provider finish reason, surfaced for diagnostics.</param>
/// <param name="InputTokens">Prompt tokens consumed, for cost telemetry.</param>
/// <param name="OutputTokens">Completion tokens produced.</param>
public sealed record AiCompletionResult(
    string? Content,
    IReadOnlyList<AiToolCall> ToolCalls,
    string? FinishReason,
    int InputTokens,
    int OutputTokens)
{
    public bool RequiresToolExecution => ToolCalls.Count > 0;
}

/// <param name="Id">Provider-assigned call id, echoed back with the result.</param>
/// <param name="ToolName">Which tool the model wants to run.</param>
/// <param name="ArgumentsJson">Raw JSON arguments, validated before use.</param>
public sealed record AiToolCall(string Id, string ToolName, string ArgumentsJson);

/// <summary>The provider-facing description of a tool.</summary>
/// <param name="Name">Tool name as the model sees it.</param>
/// <param name="Description">What it does and when to use it.</param>
/// <param name="InputSchema">JSON Schema for the arguments.</param>
public sealed record AiToolDescriptor(string Name, string Description, JsonElement InputSchema);

/// <summary>
/// Thrown when AI is requested in an environment with no provider configured. The API maps this
/// to <c>503</c> with problem type <c>ai_not_configured</c>.
/// </summary>
public sealed class AiNotConfiguredException : Exception
{
    public AiNotConfiguredException()
        : base("No AI provider is configured for this environment. " +
               "Configure Azure OpenAI to enable AI features.")
    {
    }
}
