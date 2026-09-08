using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Abstractions;
using NexaOps.Infrastructure.Persistence;

namespace NexaOps.Api.Workers;

/// <summary>
/// Sends the emails queued alongside in-product notifications.
/// <para>
/// Dispatch is deliberately out of band. A user's request is never made slower, and never fails,
/// because a mail provider is slow or unavailable. Failures are recorded on the notification row
/// with an attempt count, so a message that could not be delivered is visible rather than lost.
/// </para>
/// </summary>
public sealed class NotificationDispatchWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 50;

    /// <summary>Give up after this many attempts rather than retrying a bad address forever.</summary>
    private const int MaxAttempts = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationDispatchWorker> _logger;

    public NotificationDispatchWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationDispatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification dispatch pass failed. The next pass will retry.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                break;
            }
        }
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        // Nothing to do, and nothing was queued in the first place: the notification service only
        // sets EmailRequested when a provider exists.
        if (!email.IsConfigured)
        {
            return;
        }

        var context = scope.ServiceProvider.GetRequiredService<NexaOpsDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        // Dispatch drains the queue for every tenant in one pass.
        using var suppression = context.SuppressTenantFilter();

        var pending = await context.Notifications
            .Where(n => n.EmailRequested
                        && n.EmailSentAt == null
                        && n.EmailAttempts < MaxAttempts)
            .OrderBy(n => n.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (pending.Count == 0)
        {
            return;
        }

        var recipientIds = pending.Select(n => n.RecipientUserId).Distinct().ToList();

        var recipients = await context.Users
            .AsNoTracking()
            .Where(u => recipientIds.Contains(u.Id) && !u.IsArchived)
            .Select(u => new { u.Id, u.Email, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, cancellationToken)
            .ConfigureAwait(false);

        var sent = 0;

        foreach (var notification in pending)
        {
            notification.EmailAttempts++;

            if (!recipients.TryGetValue(notification.RecipientUserId, out var recipient))
            {
                notification.EmailRequested = false;
                notification.EmailFailureReason = "The recipient no longer exists or has been archived.";
                continue;
            }

            var result = await email.SendAsync(
                new EmailMessage
                {
                    To = recipient.Email,
                    ToDisplayName = recipient.DisplayName,
                    Subject = notification.Title,
                    HtmlBody = RenderHtml(notification.Title, notification.Body, notification.ActionUrl),
                    NotificationId = notification.Id
                },
                cancellationToken).ConfigureAwait(false);

            if (result.Succeeded)
            {
                notification.EmailSentAt = clock.UtcNow;
                notification.EmailFailureReason = null;
                sent++;
            }
            else
            {
                notification.EmailFailureReason = result.FailureReason;

                if (notification.EmailAttempts >= MaxAttempts)
                {
                    _logger.LogWarning(
                        "Giving up on notification {NotificationId} after {Attempts} attempts: {Reason}",
                        notification.Id, notification.EmailAttempts, result.FailureReason);
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (sent > 0)
        {
            _logger.LogInformation("Dispatched {Count} notification email(s).", sent);
        }
    }

    /// <summary>
    /// Renders the notification email.
    /// <para>
    /// Every interpolated value is HTML-encoded. Notification bodies contain incident titles and
    /// comment excerpts written by users, so an unencoded template here would deliver stored
    /// cross-site scripting straight into a colleague's mail client.
    /// </para>
    /// </summary>
    private static string RenderHtml(string title, string body, string? actionUrl)
    {
        var safeTitle = System.Net.WebUtility.HtmlEncode(title);
        var safeBody = System.Net.WebUtility.HtmlEncode(body);

        var action = string.IsNullOrWhiteSpace(actionUrl)
            ? string.Empty
            : $"""<p style="margin:24px 0 0"><a href="{System.Net.WebUtility.HtmlEncode(actionUrl)}" style="background:#1B4DFF;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none;font-weight:600">Open in NexaOps</a></p>""";

        return $"""
            <!doctype html>
            <html lang="en"><body style="margin:0;background:#f4f6fb;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif">
              <div style="max-width:600px;margin:32px auto;background:#fff;border-radius:12px;padding:32px;border:1px solid #e3e8f0">
                <p style="margin:0 0 4px;font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:#6b7a99">NexaOps</p>
                <h1 style="margin:0 0 16px;font-size:20px;color:#0f172a">{safeTitle}</h1>
                <p style="margin:0;font-size:15px;line-height:1.6;color:#334155">{safeBody}</p>
                {action}
                <p style="margin:32px 0 0;font-size:12px;color:#94a3b8">
                  You are receiving this because you are involved in this record in NexaOps.
                </p>
              </div>
            </body></html>
            """;
    }
}
