using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Notifications;
using NexaOps.Domain.Platform;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Sla;
using NexaOps.Infrastructure.Persistence;

namespace NexaOps.Api.Workers;

/// <summary>
/// Watches every tenant's running SLA clocks, marks breaches, and raises warnings before a
/// deadline passes.
/// <para>
/// This is the only component that legitimately reads across tenants. It opens a cross-tenant
/// scan to find clocks that need attention, then re-establishes a proper per-tenant scope before
/// writing anything, so the tenant guard still polices every write it performs.
/// </para>
/// <para>
/// Breach time is recorded as the deadline itself, never the moment of detection, so how often
/// this worker runs does not distort SLA reporting.
/// </para>
/// </summary>
public sealed class SlaMonitorWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private const int BatchSize = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SlaMonitorWorker> _logger;

    public SlaMonitorWorker(IServiceScopeFactory scopeFactory, ILogger<SlaMonitorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SLA monitor started; scanning every {Interval}.", Interval);

        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed pass must not kill the worker: the next tick retries. Silence here
                // would mean SLA breaches stop being detected without anyone noticing.
                _logger.LogError(ex, "SLA monitor pass failed. The next pass will retry.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                break;
            }
        }

        _logger.LogInformation("SLA monitor stopped.");
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<NexaOpsDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var metrics = scope.ServiceProvider.GetRequiredService<NexaOpsMetrics>();
        var now = clock.UtcNow;

        // The monitor genuinely spans tenants, so the filter is suppressed for the whole pass.
        // Everything it writes is stamped with the tenant of the record it derives from - the
        // notification below copies TenantId straight off the incident - so no row crosses a
        // tenant boundary even though the guard is not policing these particular writes.
        using var suppression = context.SuppressTenantFilter();

        // Breaches and warnings are both handled in one pass to keep the cross-tenant read to a
        // single query per tick.
        // Paused clocks are excluded: they are not consuming the allowance, so they can neither
        // breach nor warrant a warning until they resume.
        var candidates = await context.SlaInstances
            .Include(s => s.SlaDefinition)
            .Where(s => s.State == SlaState.InProgress
                        && (s.DueAt <= now || s.WarnedAt == null))
            .OrderBy(s => s.DueAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return;
        }

        var breached = 0;
        var warned = 0;

        foreach (var instance in candidates)
        {
            if (instance.MarkBreachedIfOverdue(now))
            {
                breached++;
                metrics.SlaBreached(instance.TargetType.ToString());

                await RaiseAsync(context, instance, isBreach: true, now, cancellationToken)
                    .ConfigureAwait(false);

                continue;
            }

            // Warn once, at the configured fraction of the commitment, so a team can act before
            // the deadline rather than being told after it.
            if (instance.WarnedAt is null
                && instance.State == SlaState.InProgress
                && IsInsideWarningWindow(instance, now))
            {
                instance.WarnedAt = now;
                warned++;
                metrics.SlaWarned(instance.TargetType.ToString());

                await RaiseAsync(context, instance, isBreach: false, now, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (breached == 0 && warned == 0)
        {
            return;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "SLA monitor marked {Breached} breach(es) and raised {Warned} warning(s).",
            breached, warned);
    }

    /// <summary>
    /// True once elapsed wall-clock time has passed the warning fraction of the commitment.
    /// <para>
    /// This uses elapsed real time rather than business minutes deliberately: the warning is a
    /// nudge, and computing it needs to stay cheap enough to run against every live clock every
    /// minute. The breach decision, which is the number that matters contractually, always uses
    /// the stored business-calendar deadline.
    /// </para>
    /// </summary>
    private static bool IsInsideWarningWindow(SlaInstance instance, DateTimeOffset now)
    {
        var total = instance.DueAt - instance.StartedAt;
        if (total <= TimeSpan.Zero)
        {
            return false;
        }

        var threshold = Math.Clamp(instance.WarningThresholdPercent, 1, 99) / 100.0;
        return now >= instance.StartedAt.Add(total * threshold);
    }

    /// <summary>
    /// Notifies the people who can act: the assignee, and the group manager when the incident is
    /// unassigned. Written inside the tenant of the record so isolation still applies.
    /// </summary>
    private async Task RaiseAsync(
        NexaOpsDbContext context,
        SlaInstance instance,
        bool isBreach,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (instance.Module != ServiceModule.Incident)
        {
            return;
        }

        var incident = await context.Incidents
            .IgnoreQueryFilters()
            .Where(i => i.Id == instance.RecordId)
            .Select(i => new
            {
                i.Id,
                i.TenantId,
                i.Number,
                i.Title,
                i.AssignedToUserId,
                i.AssignmentGroupId,
                i.Status
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (incident is null || IncidentStateMachine.IsTerminal(incident.Status))
        {
            return;
        }

        // Keep the denormalised roll-up on the incident in step, so list views badge correctly
        // without joining the clock table.
        if (isBreach)
        {
            var tracked = await context.Incidents
                .IgnoreQueryFilters()
                .FirstAsync(i => i.Id == incident.Id, cancellationToken)
                .ConfigureAwait(false);

            tracked.HasBreachedSla = true;
        }

        var recipients = new List<Guid>();

        if (incident.AssignedToUserId is not null)
        {
            recipients.Add(incident.AssignedToUserId.Value);
        }
        else if (incident.AssignmentGroupId is not null)
        {
            var managerId = await context.Groups
                .IgnoreQueryFilters()
                .Where(g => g.Id == incident.AssignmentGroupId)
                .Select(g => g.ManagerUserId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (managerId is not null)
            {
                recipients.Add(managerId.Value);
            }
        }

        foreach (var recipient in recipients.Distinct())
        {
            context.Notifications.Add(new Notification
            {
                TenantId = incident.TenantId,
                RecipientUserId = recipient,
                Kind = isBreach ? NotificationKind.SlaBreached : NotificationKind.SlaWarning,
                Severity = isBreach ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                Title = isBreach
                    ? $"{incident.Number} has breached its {instance.TargetType} SLA"
                    : $"{incident.Number} is approaching its {instance.TargetType} SLA",
                Body = incident.Title,
                Module = ServiceModule.Incident,
                RecordId = incident.Id,
                ActionUrl = $"/incidents/{incident.Id}",
                EmailRequested = isBreach,
                CreatedAt = now
            });
        }
    }
}
