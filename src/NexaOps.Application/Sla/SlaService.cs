using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Sla;

namespace NexaOps.Application.Sla;

/// <summary>
/// Attaches SLA clocks to records and keeps them in step with the record lifecycle.
/// </summary>
public interface ISlaService
{
    /// <summary>
    /// Attaches the response and resolution clocks that the tenant policies select for this
    /// incident. Safe to call more than once: an existing clock for a target type is left alone.
    /// </summary>
    Task AttachClocksAsync(Incident incident, CancellationToken cancellationToken = default);

    /// <summary>Pauses, resumes, completes or cancels clocks in response to a status change.</summary>
    Task OnStatusChangedAsync(
        Incident incident,
        IncidentStatus previousStatus,
        CancellationToken cancellationToken = default);

    /// <summary>Stops the response clock when an agent first replies.</summary>
    Task OnFirstResponseAsync(Incident incident, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-targets live clocks after a priority change, because the commitment for a P1 is not
    /// the commitment for a P3. Elapsed time already consumed is preserved, and a clock that is
    /// no longer overdue against the corrected commitment stops being a breach. Clocks that were
    /// genuinely met, or cancelled with the record, are left alone.
    /// </summary>
    Task OnPriorityChangedAsync(Incident incident, CancellationToken cancellationToken = default);

    /// <summary>Projects a clock into the DTO the UI renders.</summary>
    Task<SlaSnapshot> DescribeAsync(SlaInstance instance, CancellationToken cancellationToken = default);
}

/// <summary>Computed SLA figures at a point in time.</summary>
/// <param name="ElapsedMinutes">Working minutes consumed.</param>
/// <param name="RemainingMinutes">Working minutes left; negative once overrun.</param>
/// <param name="ConsumedPercent">Consumption against the commitment.</param>
public sealed record SlaSnapshot(int ElapsedMinutes, int RemainingMinutes, int ConsumedPercent);

/// <inheritdoc />
public sealed class SlaService : ISlaService
{
    private readonly ISlaRepository _slaRepository;
    private readonly IIncidentSlaWriter _writer;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<SlaService> _logger;

    public SlaService(
        ISlaRepository slaRepository,
        IIncidentSlaWriter writer,
        IDateTimeProvider clock,
        ILogger<SlaService> logger)
    {
        _slaRepository = slaRepository;
        _writer = writer;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task AttachClocksAsync(Incident incident, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var policies = await _slaRepository
            .GetActivePoliciesAsync(ServiceModule.Incident, cancellationToken)
            .ConfigureAwait(false);

        if (policies.Count == 0)
        {
            _logger.LogDebug(
                "No active SLA policies for tenant; incident {Number} has no SLA commitments.",
                incident.Number);
            return;
        }

        var existing = incident.SlaInstances
            .Where(i => !i.IsSettled)
            .Select(i => i.TargetType)
            .ToHashSet();

        // Evaluate the ordered policy list once per target type: the first policy that both
        // matches the record and carries a definition for that target type wins.
        foreach (var targetType in new[] { SlaTargetType.Response, SlaTargetType.Resolution })
        {
            if (existing.Contains(targetType))
            {
                continue;
            }

            var policy = policies.FirstOrDefault(p =>
                p.SlaDefinition is not null
                && p.SlaDefinition.TargetType == targetType
                && p.SlaDefinition.IsActive
                && p.Matches(
                    ServiceModule.Incident,
                    incident.Priority,
                    incident.CategoryId,
                    incident.SubcategoryId,
                    incident.AssignmentGroupId,
                    incident.OrganizationId));

            if (policy?.SlaDefinition is null)
            {
                continue;
            }

            var definition = policy.SlaDefinition;
            var schedule = await _slaRepository
                .GetScheduleAsync(definition.BusinessCalendarId, cancellationToken)
                .ConfigureAwait(false);

            var startedAt = incident.CreatedAt == default ? _clock.UtcNow : incident.CreatedAt;

            var instance = new SlaInstance
            {
                TenantId = incident.TenantId,
                Module = ServiceModule.Incident,
                RecordId = incident.Id,
                SlaDefinitionId = definition.Id,
                TargetType = targetType,
                SlaName = definition.Name,
                DurationMinutes = definition.DurationMinutes,
                WarningThresholdPercent = definition.WarningThresholdPercent,
                PauseWhenPending = definition.PauseWhenPending,

                // Deliberately not setting the SlaDefinition navigation. Policies are loaded
                // AsNoTracking, so attaching one to a new instance makes EF treat the definition
                // as a fresh row and try to insert it, violating its primary key.
                BusinessCalendarId = definition.BusinessCalendarId,
                StartedAt = startedAt,
                DueAt = schedule.AddBusinessMinutes(startedAt, definition.DurationMinutes),
                State = SlaState.InProgress
            };

            // A response target on an incident that already had its first response is settled
            // immediately - this happens when an agent raises and answers a call in one go.
            if (targetType == SlaTargetType.Response && incident.FirstRespondedAt is not null)
            {
                instance.Complete(incident.FirstRespondedAt.Value);
            }
            else if (targetType == SlaTargetType.Resolution && incident.ResolvedAt is not null)
            {
                instance.Complete(incident.ResolvedAt.Value);
            }

            incident.SlaInstances.Add(instance);
            _writer.AddSlaInstance(instance);

            _logger.LogInformation(
                "Attached {TargetType} SLA {SlaName} to incident {Number}, due {DueAt:u}.",
                targetType, definition.Name, incident.Number, instance.DueAt);
        }

        UpdateRollUp(incident);
    }

    /// <inheritdoc />
    public async Task OnStatusChangedAsync(
        Incident incident,
        IncidentStatus previousStatus,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var now = _clock.UtcNow;
        var wasPaused = IncidentStateMachine.PausesSla(previousStatus);
        var isPaused = IncidentStateMachine.PausesSla(incident.Status);

        foreach (var instance in incident.SlaInstances)
        {
            if (instance.IsSettled)
            {
                continue;
            }

            var schedule = await GetScheduleForAsync(instance, cancellationToken).ConfigureAwait(false);

            switch (incident.Status)
            {
                case IncidentStatus.Cancelled:
                    instance.Cancel();
                    continue;

                case IncidentStatus.Resolved or IncidentStatus.Closed
                    when instance.TargetType == SlaTargetType.Resolution:
                    // Resume first so paused time is credited before the outcome is decided.
                    if (instance.State == SlaState.Paused)
                    {
                        instance.Resume(schedule, now);
                    }

                    instance.Complete(incident.ResolvedAt ?? now);
                    continue;
            }

            if (isPaused && !wasPaused && ShouldPause(instance))
            {
                instance.Pause(now);
            }
            else if (!isPaused && wasPaused && instance.State == SlaState.Paused)
            {
                instance.Resume(schedule, now);
            }

            // Reopening a resolved incident restarts the resolution commitment from where it
            // left off; the clock object is reused so the original start time is preserved.
            if (previousStatus == IncidentStatus.Resolved
                && incident.Status == IncidentStatus.InProgress
                && instance.TargetType == SlaTargetType.Resolution
                && instance.State == SlaState.Paused)
            {
                instance.Resume(schedule, now);
            }
        }

        UpdateRollUp(incident);
    }

    /// <inheritdoc />
    public async Task OnFirstResponseAsync(Incident incident, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(incident);

        if (incident.FirstRespondedAt is null)
        {
            return;
        }

        foreach (var instance in incident.SlaInstances
                     .Where(i => i.TargetType == SlaTargetType.Response && !i.IsSettled))
        {
            instance.Complete(incident.FirstRespondedAt.Value);
        }

        UpdateRollUp(incident);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task OnPriorityChangedAsync(Incident incident, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var policies = await _slaRepository
            .GetActivePoliciesAsync(ServiceModule.Incident, cancellationToken)
            .ConfigureAwait(false);

        // Breached clocks are included deliberately. If triage decides an incident was never a
        // P1, the P1 commitment it blew should not stand; a clock that was genuinely met, or
        // cancelled with the record, is finished and is left alone.
        var reTargetable = incident.SlaInstances
            .Where(i => i.State is SlaState.InProgress or SlaState.Paused or SlaState.Breached)
            .ToList();

        foreach (var instance in reTargetable)
        {
            var policy = policies.FirstOrDefault(p =>
                p.SlaDefinition is not null
                && p.SlaDefinition.TargetType == instance.TargetType
                && p.SlaDefinition.IsActive
                && p.Matches(
                    ServiceModule.Incident,
                    incident.Priority,
                    incident.CategoryId,
                    incident.SubcategoryId,
                    incident.AssignmentGroupId,
                    incident.OrganizationId));

            if (policy?.SlaDefinition is null || policy.SlaDefinitionId == instance.SlaDefinitionId)
            {
                continue;
            }

            var definition = policy.SlaDefinition;
            var schedule = await _slaRepository
                .GetScheduleAsync(definition.BusinessCalendarId, cancellationToken)
                .ConfigureAwait(false);

            var previousName = instance.SlaName;

            instance.SlaDefinitionId = definition.Id;
            instance.SlaName = definition.Name;
            instance.DurationMinutes = definition.DurationMinutes;
            instance.WarningThresholdPercent = definition.WarningThresholdPercent;
            instance.PauseWhenPending = definition.PauseWhenPending;
            instance.BusinessCalendarId = definition.BusinessCalendarId;

            // Re-target from the original start so that time already spent still counts against
            // the new, tighter or looser commitment. Paused minutes are added back on top.
            instance.DueAt = schedule.AddBusinessMinutes(
                instance.StartedAt,
                definition.DurationMinutes + instance.PausedBusinessMinutes);

            // A clock that is no longer overdue against the new target stops being a breach.
            if (instance.State == SlaState.Breached && _clock.UtcNow <= instance.DueAt)
            {
                instance.BreachedAt = null;
                instance.State = instance.PausedAt is not null ? SlaState.Paused : SlaState.InProgress;
            }

            instance.WarnedAt = null;

            _logger.LogInformation(
                "Re-targeted {TargetType} SLA on incident {Number} from {Previous} to {Current} after priority change to {Priority}.",
                instance.TargetType, incident.Number, previousName, definition.Name, incident.Priority);
        }

        UpdateRollUp(incident);
    }

    /// <inheritdoc />
    public async Task<SlaSnapshot> DescribeAsync(
        SlaInstance instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var schedule = await GetScheduleForAsync(instance, cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;

        return new SlaSnapshot(
            instance.ElapsedBusinessMinutes(schedule, now),
            instance.RemainingBusinessMinutes(schedule, now),
            instance.ConsumedPercent(schedule, now));
    }

    private Task<BusinessSchedule> GetScheduleForAsync(SlaInstance instance, CancellationToken cancellationToken)
        => _slaRepository.GetScheduleAsync(instance.BusinessCalendarId, cancellationToken);

    private static bool ShouldPause(SlaInstance instance)
    {
        // A response commitment measures time to first contact. Waiting on the requester before
        // anyone has replied is still the service desk's silence, so response clocks never pause.
        if (instance.TargetType == SlaTargetType.Response)
        {
            return false;
        }

        return instance.PauseWhenPending;
    }

    /// <summary>
    /// Refreshes the denormalised SLA fields on the incident so list views can badge and sort
    /// without joining the clock table.
    /// </summary>
    private static void UpdateRollUp(Incident incident)
    {
        var live = incident.SlaInstances.Where(i => !i.IsSettled).ToList();

        incident.HasBreachedSla = incident.SlaInstances.Any(i => i.BreachedAt is not null);
        incident.NextSlaDueAt = live.Count == 0 ? null : live.Min(i => i.DueAt);
    }
}

/// <summary>
/// The narrow slice of persistence the SLA service needs. Kept separate from
/// <see cref="Incidents.IIncidentRepository"/> so the SLA engine can be reused by the request,
/// problem and change modules without dragging incident persistence along.
/// </summary>
public interface IIncidentSlaWriter
{
    void AddSlaInstance(SlaInstance instance);
}
