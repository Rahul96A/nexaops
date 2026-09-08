namespace NexaOps.Application.Abstractions;

/// <summary>
/// Publishes integration events to Azure Service Bus for out-of-process consumers - Azure
/// Functions, webhooks and future modules.
/// <para>
/// Publishing happens after the transaction commits. An event describing a change that was
/// rolled back would be a lie other systems could not detect.
/// </para>
/// </summary>
public interface IEventPublisher
{
    /// <summary>True when a broker is configured. False in local development by default.</summary>
    bool IsConfigured { get; }

    Task PublishAsync<T>(string topic, T payload, CancellationToken cancellationToken = default) where T : class;
}
