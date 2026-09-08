namespace NexaOps.Application.Abstractions;

/// <summary>
/// Outbound email port. Azure Communication Services in Azure.
/// <para>
/// When email is not configured the implementation records the attempt and reports failure
/// rather than pretending to have sent: a notification that silently vanishes is worse than
/// one that visibly did not go.
/// </para>
/// </summary>
public interface IEmailSender
{
    /// <summary>True when a provider is configured. The UI uses this to explain why email is off.</summary>
    bool IsConfigured { get; }

    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>An outbound message. Bodies are rendered server-side from templates.</summary>
public sealed class EmailMessage
{
    public required string To { get; init; }
    public string? ToDisplayName { get; init; }
    public required string Subject { get; init; }
    public required string HtmlBody { get; init; }
    public string? PlainTextBody { get; init; }

    /// <summary>Ties the message back to the notification row for delivery tracking.</summary>
    public Guid? NotificationId { get; init; }
}

/// <param name="Succeeded">Whether the provider accepted the message.</param>
/// <param name="ProviderMessageId">Provider identifier for support conversations.</param>
/// <param name="FailureReason">Why it failed. Non-null exactly when <paramref name="Succeeded"/> is false.</param>
public sealed record EmailSendResult(bool Succeeded, string? ProviderMessageId, string? FailureReason)
{
    public static EmailSendResult Success(string? id = null) => new(true, id, null);
    public static EmailSendResult Failure(string reason) => new(false, null, reason);
}
