using System.ClientModel;
using System.Text.Json;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaOps.Application.Ai;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace NexaOps.Infrastructure.Ai;

/// <summary>
/// Azure AI configuration.
/// <para>
/// The API key property exists only for local development. In every deployed environment
/// <see cref="UseManagedIdentity"/> is true and no key exists anywhere - not in configuration,
/// not in the container image, not in an app setting.
/// </para>
/// </summary>
public sealed class AzureAiOptions
{
    public const string SectionName = "AzureAi";

    /// <summary>Azure OpenAI endpoint, e.g. <c>https://aoai-nexaops-prod.openai.azure.com/</c>.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Chat model deployment name, e.g. <c>gpt-4o</c>.</summary>
    public string? ChatDeployment { get; set; }

    /// <summary>Embedding model deployment name, e.g. <c>text-embedding-3-large</c>.</summary>
    public string? EmbeddingDeployment { get; set; }

    /// <summary>True in Azure. The credential is the app's Managed Identity.</summary>
    public bool UseManagedIdentity { get; set; } = true;

    /// <summary>
    /// Local development only, supplied through user secrets. Never committed, never returned
    /// by any API, and never sent to the browser.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Azure AI Search endpoint, used for retrieval-augmented generation.</summary>
    public string? SearchEndpoint { get; set; }

    /// <summary>Azure AI Search index holding tenant-scoped knowledge documents.</summary>
    public string? SearchIndexName { get; set; }

    /// <summary>Ceiling on tool-calling rounds per assistant turn, so a loop cannot run away.</summary>
    public int MaxToolIterations { get; set; } = 5;

    /// <summary>True when enough is configured to talk to a chat model.</summary>
    public bool IsChatConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(ChatDeployment)
        && (UseManagedIdentity || !string.IsNullOrWhiteSpace(ApiKey));

    public bool IsEmbeddingConfigured => IsChatConfigured && !string.IsNullOrWhiteSpace(EmbeddingDeployment);
}

/// <summary>
/// Azure OpenAI adapter.
/// <para>
/// When Azure OpenAI is not configured this reports <see cref="IsConfigured"/> false and every
/// call throws <see cref="AiNotConfiguredException"/>, which the API surfaces as a clear 503.
/// It never returns a canned answer: an environment without AI shows AI as unavailable rather
/// than simulating intelligence it does not have.
/// </para>
/// </summary>
public sealed class AzureOpenAiCompletionService : IAiCompletionService
{
    private readonly AzureOpenAIClient? _client;
    private readonly AzureAiOptions _options;
    private readonly ILogger<AzureOpenAiCompletionService> _logger;

    public AzureOpenAiCompletionService(
        AzureOpenAIClient? client,
        IOptions<AzureAiOptions> options,
        ILogger<AzureOpenAiCompletionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsConfigured => _client is not null && _options.IsChatConfigured;

    /// <inheritdoc />
    public AiProviderInfo ProviderInfo => IsConfigured
        ? new AiProviderInfo(
            "AzureOpenAI",
            _options.ChatDeployment,
            _options.EmbeddingDeployment,
            _options.IsEmbeddingConfigured)
        : AiProviderInfo.NotConfigured;

    /// <inheritdoc />
    public async Task<AiCompletionResult> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsConfigured)
        {
            throw new AiNotConfiguredException();
        }

        var chat = _client!.GetChatClient(_options.ChatDeployment);
        var messages = request.Messages.Select(ToChatMessage).ToList();

        var options = new ChatCompletionOptions
        {
            Temperature = request.Temperature,
            MaxOutputTokenCount = request.MaxOutputTokens
        };

        foreach (var tool in request.Tools)
        {
            options.Tools.Add(ChatTool.CreateFunctionTool(
                tool.Name,
                tool.Description,
                BinaryData.FromString(tool.InputSchema.GetRawText())));
        }

        try
        {
            var response = await chat.CompleteChatAsync(messages, options, cancellationToken)
                .ConfigureAwait(false);

            var completion = response.Value;

            var toolCalls = completion.ToolCalls
                .Select(c => new AiToolCall(c.Id, c.FunctionName, c.FunctionArguments.ToString()))
                .ToList();

            var text = completion.Content.Count > 0
                ? string.Concat(completion.Content.Select(p => p.Text))
                : null;

            return new AiCompletionResult(
                string.IsNullOrWhiteSpace(text) ? null : text,
                toolCalls,
                completion.FinishReason.ToString(),
                completion.Usage?.InputTokenCount ?? 0,
                completion.Usage?.OutputTokenCount ?? 0);
        }
        catch (ClientResultException ex)
        {
            // Provider detail stays server-side. Relaying it would leak deployment names and
            // quota information into a user-visible answer.
            _logger.LogError(ex, "Azure OpenAI chat completion failed with status {Status}.", ex.Status);
            throw new InvalidOperationException("The AI service could not complete the request.", ex);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float>> EmbedAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (_client is null || !_options.IsEmbeddingConfigured)
        {
            throw new AiNotConfiguredException();
        }

        var embeddings = _client.GetEmbeddingClient(_options.EmbeddingDeployment);

        var response = await embeddings.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return response.Value.ToFloats().ToArray();
    }

    private static ChatMessage ToChatMessage(AiMessage message) => message.Role switch
    {
        "system" => new SystemChatMessage(message.Content),
        "assistant" => new AssistantChatMessage(message.Content),
        "tool" => new ToolChatMessage(message.ToolCallId ?? string.Empty, message.Content),
        _ => new UserChatMessage(message.Content)
    };
}

/// <summary>
/// The adapter used when no AI provider is configured.
/// <para>
/// Registered in place of the Azure adapter so that AI endpoints return a clear "not
/// configured" response instead of failing with a null reference, and so that no code path
/// exists that could return a fabricated answer.
/// </para>
/// </summary>
public sealed class UnconfiguredAiCompletionService : IAiCompletionService
{
    /// <inheritdoc />
    public bool IsConfigured => false;

    /// <inheritdoc />
    public AiProviderInfo ProviderInfo => AiProviderInfo.NotConfigured;

    /// <inheritdoc />
    public Task<AiCompletionResult> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken cancellationToken = default)
        => throw new AiNotConfiguredException();

    /// <inheritdoc />
    public Task<IReadOnlyList<float>> EmbedAsync(string text, CancellationToken cancellationToken = default)
        => throw new AiNotConfiguredException();
}
