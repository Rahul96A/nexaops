using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Domain.Requests;
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
    Task AttachClocksAsync(ISlaTracked record, CancellationToken cancellationToken = default);

    /// <summary>Pauses, resumes, completes or cancels clocks in response to a status change.</summary>
    Task OnStatusChangedAsync(
        ISlaTracked record,
        SlaStatusChange change,
        CancellationToken cancellationToken = default);

    /// <summary>Stops the response clock when an agent first replies.</summary>
    /// <summary>
    /// Settles the response clock at the moment first contact was made. Modules with no
    /// response commitment simply never call it.
    /// </summary>
    Task OnFirstResponseAsync(
        ISlaTracked record,
        DateTimeOffset respondedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-targets live clocks after a priority change, because the commitment for a P1 is not
    /// the commitment for a P3. Elapsed time already consumed is preserved, and a clock that is
    /// no longer overdue against the corrected commitment stops being a breach. Clocks that were
    /// genuinely met, or cancelled with the record, are left alone.
    /// </summary>
    Task OnPriorityChangedAsync(ISlaTracked record, CancellationToken cancellationToken = default);

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
    private readonly ISlaInstanceWriter _writer;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<SlaService> _logger;

    public SlaService(
        ISlaRepository slaRepository,
        ISlaInstanceWriter writer,
        IDateTimeProvider clock,
        ILogger<SlaService> logger)
    {
        _slaRepository = slaRepository;
        _writer = writer;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task AttachClocksAsync(ISlaTracked record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var policies = await _slaRepository
            .GetActivePoliciesAsync(record.SlaModule, cancellationToken)
            .ConfigureAwait(false);

        if (policies.Count == 0)
        {
            _logger.LogDebug(
                "No active SLA policies for {Module}; {Number} has no SLA commitments.",
                record.SlaModule, record.Number);
            return;
        }

        var existing = record.SlaInstances
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
                    record.SlaModule,
                    record.Priority,
                    record.CategoryId,
                    record.SlaSubcategoryId,
                    record.SlaGroupId,
                    record.OrganizationId));

            if (policy?.SlaDefinition is null)
            {
                continue;
            }

            var definition = policy.SlaDefinition;
            var schedule = await _slaRepository
                .GetScheduleAsync(definition.BusinessCalendarId, cancellationToken)
                .ConfigureAwait(false);

            var startedAt = record.CreatedAt == default ? _clock.UtcNow : record.CreatedAt;

            var instance = new SlaInstance
            {
                TenantId = record.TenantId,
                Module = record.SlaModule,
                RecordId = record.Id,
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

            // A record that was already answered or completed before its clocks were attached
            // settles them immediately - an agent who raises and answers a call in one go, or a
            // request fulfilled the moment it was approved.
            if (record is ISlaProgressFacts facts)
            {
                if (targetType == SlaTargetType.Response && facts.FirstRespondedAt is not null)
                {
                    instance.Complete(facts.FirstRespondedAt.Value);
                }
                else if (targetType == SlaTargetType.Resolution && facts.SlaCompletedAt is not null)
                {
                    instance.Complete(facts.SlaCompletedAt.Value);
                }
            }

            record.SlaInstances.Add(instance);
            _writer.AddSlaInstance(instance);

            _logger.LogInformation(
                "Attached {TargetType} SLA {SlaName} to {Number}, due {DueAt:u}.",
                targetType, definition.Name, record.Number, instance.DueAt);
        }

        UpdateRollUp(record);
    }

    /// <inheritdoc />
    public async Task OnStatusChangedAsync(
        ISlaTracked record,
        SlaStatusChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(change);

        var now = _clock.UtcNow;
        var wasPaused = change.WasPaused;
        var isPaused = change.IsPaused;

        foreach (var instance in record.SlaInstances)
        {
            if (instance.IsSettled)
            {
                continue;
            }

            var schedule = await GetScheduleForAsync(instance, cancellationToken).ConfigureAwait(false);

            if (change.IsCancelled)
            {
                // Abandoned, not breached. Nobody failed a commitment on work that was called off.
                instance.Cancel();
                continue;
            }

            if (change.IsCompleted && instance.TargetType == SlaTargetType.Resolution)
            {
                // Resume first so paused time is credited before the outcome is decided.
                if (instance.State == SlaState.Paused)
                {
                    instance.Resume(schedule, now);
                }

                instance.Complete(change.CompletedAt ?? now);
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

            // Reopening completed work restarts the resolution commitment from where it left
            // off; the clock object is reused so the original start time is preserved.
            if (change.IsReopened
                && instance.TargetType == SlaTargetType.Resolution
                && instance.State == SlaState.Paused)
            {
                instance.Resume(schedule, now);
            }
        }

        UpdateRollUp(record);
    }

    /// <inheritdoc />
    public async Task OnFirstResponseAsync(
        ISlaTracked record,
        DateTimeOffset respondedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        foreach (var instance in record.SlaInstances
                     .Where(i => i.TargetType == SlaTargetType.Response && !i.IsSettled))
        {
            instance.Complete(respondedAt);
        }

        UpdateRollUp(record);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task OnPriorityChangedAsync(ISlaTracked record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var policies = await _slaRepository
            .GetActivePoliciesAsync(record.SlaModule, cancellationToken)
            .ConfigureAwait(false);

        // Breached clocks are included deliberately. If triage decides an incident was never a
        // P1, the P1 commitment it blew should not stand; a clock that was genuinely met, or
        // cancelled with the record, is finished and is left alone.
        var reTargetable = record.SlaInstances
            .Where(i => i.State is SlaState.InProgress or SlaState.Paused or SlaState.Breached)
            .ToList();

        foreach (var instance in reTargetable)
        {
            var policy = policies.FirstOrDefault(p =>
                p.SlaDefinition is not null
                && p.SlaDefinition.TargetType == instance.TargetType
                && p.SlaDefinition.IsActive
                && p.Matches(
                    record.SlaModule,
                    record.Priority,
                    record.CategoryId,
                    record.SlaSubcategoryId,
                    record.SlaGroupId,
                    record.OrganizationId));

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
                "Re-targeted {TargetType} SLA on {Number} from {Previous} to {Current} after priority change to {Priority}.",
                instance.TargetType, record.Number, previousName, definition.Name, record.Priority);
        }

        UpdateRollUp(record);
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
    private static void UpdateRollUp(ISlaTracked record)
    {
        var live = record.SlaInstances.Where(i => !i.IsSettled).ToList();

        record.HasBreachedSla = record.SlaInstances.Any(i => i.BreachedAt is not null);
        record.NextSlaDueAt = live.Count == 0 ? null : live.Min(i => i.DueAt);
    }
}

/// <summary>
/// The narrow slice of persistence the SLA service needs. Kept separate from any module's
/// repository so the engine can be reused by requests, problems and changes without dragging
/// one module's persistence along.
/// </summary>
public interface ISlaInstanceWriter
{
    void AddSlaInstance(SlaInstance instance);
}

/// <summary>
/// Translates the incident lifecycle into the vocabulary the SLA engine understands.
/// <para>
/// Extracted so there is exactly one mapping. If the tests restated it, they would verify a
/// copy rather than the rule the product actually applies.
/// </para>
/// </summary>
public static class IncidentSlaMapping
{
    public static SlaStatusChange For(Incident incident, IncidentStatus previousStatus)
    {
        ArgumentNullException.ThrowIfNull(incident);

        return new SlaStatusChange(
            WasPaused: IncidentStateMachine.PausesSla(previousStatus),
            IsPaused: IncidentStateMachine.PausesSla(incident.Status),
            IsCancelled: incident.Status == IncidentStatus.Cancelled,
            IsCompleted: incident.Status is IncidentStatus.Resolved or IncidentStatus.Closed,
            CompletedAt: incident.ResolvedAt,
            IsReopened: previousStatus == IncidentStatus.Resolved
                        && incident.Status == IncidentStatus.InProgress);
    }
}

/// <summary>
/// Translates the service request lifecycle into the vocabulary the SLA engine understands.
/// <para>
/// The counterpart to <see cref="IncidentSlaMapping"/>. The two modules differ where the
/// business differs - a request pauses while it waits for an approver, an incident has no such
/// state - and that difference lives here rather than inside the engine.
/// </para>
/// </summary>
public static class RequestSlaMapping
{
    public static SlaStatusChange For(ServiceRequest request, RequestStatus previousStatus)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new SlaStatusChange(
            WasPaused: RequestStateMachine.PausesSla(previousStatus),
            IsPaused: RequestStateMachine.PausesSla(request.Status),
            IsCancelled: request.Status is RequestStatus.Cancelled or RequestStatus.Rejected,
            IsCompleted: request.Status is RequestStatus.Fulfilled or RequestStatus.Closed,
            CompletedAt: request.FulfilledAt,
            IsReopened: previousStatus == RequestStatus.Fulfilled
                        && request.Status == RequestStatus.InProgress);
    }
}
