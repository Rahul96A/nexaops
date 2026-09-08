using System.Text.Json;
using Azure.Communication.Email;
using AcsEmailMessage = Azure.Communication.Email.EmailMessage;
using NexaOpsEmailMessage = NexaOps.Application.Abstractions.EmailMessage;
using NexaOpsEmailSendResult = NexaOps.Application.Abstractions.EmailSendResult;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexaOps.Application.Abstractions;

namespace NexaOps.Infrastructure.Messaging;

/// <summary>Outbound email configuration, backed by Azure Communication Services.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>ACS endpoint. Used with Managed Identity in Azure.</summary>
    public string? Endpoint { get; set; }

    /// <summary>ACS connection string. Local development only; Azure uses Managed Identity.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Verified sender address, e.g. <c>servicedesk@notifications.example.in</c>.</summary>
    public string? FromAddress { get; set; }

    public string FromDisplayName { get; set; } = "NexaOps Service Desk";

    /// <summary>
    /// When set, every message is redirected here instead of the real recipient. Intended for
    /// staging, so a test run cannot email real customers.
    /// </summary>
    public string? RedirectAllTo { get; set; }
}

/// <summary>
/// Azure Communication Services email adapter.
/// <para>
/// When no provider is configured this reports <see cref="IsConfigured"/> false and every send
/// returns an explicit failure. It never claims to have sent a message it did not send: a
/// notification that silently vanishes is worse than one that visibly did not go.
/// </para>
/// </summary>
public sealed class AzureEmailSender : IEmailSender
{
    private readonly EmailClient? _client;
    private readonly EmailOptions _options;
    private readonly ILogger<AzureEmailSender> _logger;

    public AzureEmailSender(
        EmailClient? client,
        IOptions<EmailOptions> options,
        ILogger<AzureEmailSender> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsConfigured => _client is not null && !string.IsNullOrWhiteSpace(_options.FromAddress);

    /// <inheritdoc />
    public async Task<NexaOpsEmailSendResult> SendAsync(
        NexaOpsEmailMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!IsConfigured)
        {
            return NexaOpsEmailSendResult.Failure(
                "No email provider is configured in this environment.");
        }

        var recipient = string.IsNullOrWhiteSpace(_options.RedirectAllTo)
            ? message.To
            : _options.RedirectAllTo;

        try
        {
            var content = new EmailContent(message.Subject)
            {
                Html = message.HtmlBody,
                PlainText = message.PlainTextBody ?? StripHtml(message.HtmlBody)
            };

            var operation = await _client!.SendAsync(
                Azure.WaitUntil.Started,
                new AcsEmailMessage(
                    _options.FromAddress,
                    new EmailRecipients([new EmailAddress(recipient, message.ToDisplayName)]),
                    content),
                cancellationToken).ConfigureAwait(false);

            return NexaOpsEmailSendResult.Success(operation.Id);
        }
        catch (Azure.RequestFailedException ex)
        {
            _logger.LogError(ex, "Email send failed with status {Status}.", ex.Status);
            return NexaOpsEmailSendResult.Failure($"Provider rejected the message: {ex.Status}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Email send failed.");
            return NexaOpsEmailSendResult.Failure("The message could not be sent.");
        }
    }

    /// <summary>
    /// Crude tag strip for the plain-text alternative. Bodies are rendered from our own
    /// templates, so this never has to cope with arbitrary markup.
    /// </summary>
    private static string StripHtml(string html)
        => System.Text.RegularExpressions.Regex.Replace(
            html,
            "<[^>]+>",
            " ",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(1)).Trim();
}

/// <summary>Azure Service Bus configuration.</summary>
public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    /// <summary>Service Bus namespace, e.g. <c>sb-nexaops-prod.servicebus.windows.net</c>.</summary>
    public string? FullyQualifiedNamespace { get; set; }

    /// <summary>Connection string. Local development only.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Prefix applied to every topic name, so environments can share a namespace.</summary>
    public string TopicPrefix { get; set; } = "nexaops";
}

/// <summary>
/// Publishes integration events to Azure Service Bus.
/// <para>
/// Reports <see cref="IsConfigured"/> false when no broker is present, and logs rather than
/// throwing so that a missing broker degrades outbound integration without failing the user's
/// request. The event is not silently dropped: it is logged at warning level.
/// </para>
/// </summary>
public sealed class ServiceBusEventPublisher : IEventPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ServiceBusClient? _client;
    private readonly MessagingOptions _options;
    private readonly ITenantContext _tenant;
    private readonly ICorrelationContext _correlation;
    private readonly ILogger<ServiceBusEventPublisher> _logger;
    private readonly Dictionary<string, ServiceBusSender> _senders = [];
    private readonly SemaphoreSlim _senderLock = new(1, 1);

    public ServiceBusEventPublisher(
        ServiceBusClient? client,
        IOptions<MessagingOptions> options,
        ITenantContext tenant,
        ICorrelationContext correlation,
        ILogger<ServiceBusEventPublisher> logger)
    {
        _client = client;
        _options = options.Value;
        _tenant = tenant;
        _correlation = correlation;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsConfigured => _client is not null;

    /// <inheritdoc />
    public async Task PublishAsync<T>(
        string topic,
        T payload,
        CancellationToken cancellationToken = default) where T : class
    {
        if (_client is null)
        {
            _logger.LogWarning(
                "Service Bus is not configured; the event for topic {Topic} was not published.",
                topic);
            return;
        }

        var fullTopic = $"{_options.TopicPrefix}-{topic}";

        try
        {
            var sender = await GetSenderAsync(fullTopic).ConfigureAwait(false);

            var message = new ServiceBusMessage(JsonSerializer.Serialize(payload, JsonOptions))
            {
                ContentType = "application/json",
                Subject = topic,
                CorrelationId = _correlation.CorrelationId,

                // Tenant travels as a message property so downstream consumers can filter and,
                // more importantly, can assert isolation on their own side.
                ApplicationProperties =
                {
                    ["tenantId"] = _tenant.HasTenant ? _tenant.TenantId.ToString() : null,
                    ["publishedAt"] = DateTimeOffset.UtcNow.ToString("O")
                }
            };

            await sender.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (ServiceBusException ex)
        {
            // Integration delivery must not fail the user's request. The failure is loud in the
            // logs and in Application Insights instead.
            _logger.LogError(ex, "Failed to publish an event to topic {Topic}.", fullTopic);
        }
    }

    private async Task<ServiceBusSender> GetSenderAsync(string topic)
    {
        if (_senders.TryGetValue(topic, out var existing))
        {
            return existing;
        }

        await _senderLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_senders.TryGetValue(topic, out existing))
            {
                return existing;
            }

            var sender = _client!.CreateSender(topic);
            _senders[topic] = sender;
            return sender;
        }
        finally
        {
            _senderLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
        {
            await sender.DisposeAsync().ConfigureAwait(false);
        }

        _senders.Clear();
        _senderLock.Dispose();
    }
}
