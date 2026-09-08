using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Notifications;
using NexaOps.Domain.Platform;
using NexaOps.Infrastructure.Persistence;

namespace NexaOps.Infrastructure.Notifications;

/// <inheritdoc />
public sealed class NotificationService : INotificationService
{
    private readonly NexaOpsDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenant;
    private readonly IDateTimeProvider _clock;
    private readonly IEmailSender _email;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        NexaOpsDbContext context,
        ICurrentUser currentUser,
        ITenantContext tenant,
        IDateTimeProvider clock,
        IEmailSender email,
        ILogger<NotificationService> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _tenant = tenant;
        _clock = clock;
        _email = email;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Notify(NotificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RecipientUserId == Guid.Empty)
        {
            return;
        }

        // Nobody needs to be notified about their own action.
        if (request.ActorUserId is not null && request.ActorUserId == request.RecipientUserId)
        {
            return;
        }

        var actionUrl = SanitiseActionUrl(request.ActionUrl);

        _context.Notifications.Add(new Notification
        {
            TenantId = _tenant.TenantId,
            RecipientUserId = request.RecipientUserId,
            Kind = request.Kind,
            Severity = request.Severity,
            Title = Truncate(request.Title, 256),
            Body = Truncate(request.Body, 4000),
            Module = request.Module,
            RecordId = request.RecordId,
            ActionUrl = actionUrl,

            // Only queue email when a provider exists. Queuing into a void would leave rows
            // that look pending forever and make the dispatcher's backlog meaningless.
            EmailRequested = request.SendEmail && _email.IsConfigured,
            CreatedAt = _clock.UtcNow
        });
    }

    /// <inheritdoc />
    public void NotifyMany(IEnumerable<Guid> recipientUserIds, NotificationRequest template)
    {
        ArgumentNullException.ThrowIfNull(recipientUserIds);
        ArgumentNullException.ThrowIfNull(template);

        foreach (var recipient in recipientUserIds.Where(r => r != Guid.Empty).Distinct())
        {
            Notify(new NotificationRequest
            {
                RecipientUserId = recipient,
                ActorUserId = template.ActorUserId,
                Kind = template.Kind,
                Severity = template.Severity,
                Title = template.Title,
                Body = template.Body,
                Module = template.Module,
                RecordId = template.RecordId,
                ActionUrl = template.ActionUrl,
                SendEmail = template.SendEmail
            });
        }
    }

    /// <inheritdoc />
    public async Task<PagedResult<NotificationDto>> GetMineAsync(
        NotificationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var userId = _currentUser.UserId;

        var source = _context.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientUserId == userId && !n.IsArchived);

        if (query.UnreadOnly)
        {
            source = source.Where(n => !n.IsRead);
        }

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await source
            .OrderByDescending(n => n.CreatedAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(n => new NotificationDto(
                n.Id,
                n.Kind,
                n.Severity,
                n.Title,
                n.Body,
                n.Module,
                n.RecordId,
                n.ActionUrl,
                n.IsRead,
                n.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<NotificationDto>(items, total, query.Page, query.PageSize);
    }

    /// <inheritdoc />
    public Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;

        return _context.Notifications
            .AsNoTracking()
            .CountAsync(n => n.RecipientUserId == userId && !n.IsRead && !n.IsArchived, cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;

        // The recipient predicate is part of the lookup, so one user cannot mark another user's
        // notification read by guessing an id.
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(
                n => n.Id == notificationId && n.RecipientUserId == userId,
                cancellationToken)
            .ConfigureAwait(false);

        if (notification is null || notification.IsRead)
        {
            return;
        }

        notification.IsRead = true;
        notification.ReadAt = _clock.UtcNow;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;
        var now = _clock.UtcNow;

        var updated = await _context.Notifications
            .Where(n => n.RecipientUserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(n => n.IsRead, true)
                    .SetProperty(n => n.ReadAt, now),
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug("Marked {Count} notifications read for user {UserId}.", updated, userId);
    }

    /// <summary>
    /// Keeps action URLs to in-app relative paths. An absolute URL here would be an
    /// open-redirect vector delivered straight into a trusted notification.
    /// </summary>
    private static string? SanitiseActionUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var trimmed = url.Trim();

        return trimmed.StartsWith('/') && !trimmed.StartsWith("//", StringComparison.Ordinal)
            ? Truncate(trimmed, 512)
            : null;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
