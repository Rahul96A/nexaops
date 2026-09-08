using NexaOps.Application.Common;
using NexaOps.Domain.Platform;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Application.Notifications;

/// <summary>
/// Creates in-product notifications and, where the recipient's preferences and the environment
/// allow, queues an email.
/// <para>
/// Notifications are written in the caller's unit of work so they commit with the change they
/// describe. Email is dispatched afterwards by a background worker, so a slow or unavailable
/// mail provider can never fail or delay the user's request.
/// </para>
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Queues a notification for one recipient. Notifying the actor about their own action is
    /// suppressed - people do not need to be told what they just did.
    /// </summary>
    void Notify(NotificationRequest request);

    /// <summary>Queues the same notification for several recipients, de-duplicating the list.</summary>
    void NotifyMany(IEnumerable<Guid> recipientUserIds, NotificationRequest template);

    Task<PagedResult<NotificationDto>> GetMineAsync(
        NotificationQuery query,
        CancellationToken cancellationToken = default);

    Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default);

    /// <summary>Marks one of the caller's own notifications read. Silently ignores others' rows.</summary>
    Task MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default);

    Task MarkAllReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Everything needed to raise one notification.</summary>
public sealed class NotificationRequest
{
    public Guid RecipientUserId { get; set; }
    public NotificationKind Kind { get; set; }
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Information;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public ServiceModule? Module { get; set; }
    public Guid? RecordId { get; set; }

    /// <summary>Relative in-app route. Absolute URLs are rejected to avoid open-redirect bait.</summary>
    public string? ActionUrl { get; set; }

    /// <summary>Whether to also send email, subject to the environment having a mail provider.</summary>
    public bool SendEmail { get; set; }

    /// <summary>
    /// The user who caused this. When it equals the recipient the notification is dropped.
    /// </summary>
    public Guid? ActorUserId { get; set; }
}

public sealed class NotificationQuery : PagedQuery
{
    public bool UnreadOnly { get; set; }
}

public sealed record NotificationDto(
    Guid Id,
    NotificationKind Kind,
    NotificationSeverity Severity,
    string Title,
    string Body,
    ServiceModule? Module,
    Guid? RecordId,
    string? ActionUrl,
    bool IsRead,
    DateTimeOffset CreatedAt);
