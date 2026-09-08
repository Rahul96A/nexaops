using NexaOps.Domain.Common;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Domain.Sla;

/// <summary>
/// A live SLA clock attached to one record. An incident normally carries two: a response
/// clock and a resolution clock.
/// <para>
/// The instance stores the calculated <see cref="DueAt"/> rather than recomputing it on every
/// read. That matters commercially: if an administrator edits a business calendar next month,
/// the commitments already made to customers do not silently move.
/// </para>
/// </summary>
public class SlaInstance : TenantEntity
{
    public ServiceModule Module { get; set; } = ServiceModule.Incident;

    /// <summary>Identifier of the governed record (the incident id, for the incident module).</summary>
    public Guid RecordId { get; set; }

    public Guid SlaDefinitionId { get; set; }
    public SlaTargetType TargetType { get; set; }

    /// <summary>Denormalised so list views can label the clock without joining the definition.</summary>
    public string SlaName { get; set; } = string.Empty;

    /// <summary>The commitment in business minutes, copied at attach time.</summary>
    public int DurationMinutes { get; set; }

    public int WarningThresholdPercent { get; set; } = 80;

    /// <summary>
    /// Whether this clock stops while the record is waiting on someone outside the service desk.
    /// <para>
    /// Copied from the definition when the clock is attached, for the same reason
    /// <see cref="DurationMinutes"/> is: the commitment made to the customer must not change
    /// because an administrator edited a definition afterwards. Reading it through the
    /// definition navigation also meant the value silently defaulted whenever that navigation
    /// was not loaded.
    /// </para>
    /// </summary>
    public bool PauseWhenPending { get; set; } = true;

    /// <summary>
    /// Business calendar this commitment is counted against, copied when the clock is attached.
    /// Null means the tenant default calendar.
    /// <para>
    /// Held on the clock rather than read through <see cref="SlaDefinition"/> so that the
    /// schedule behind an existing commitment cannot change because an administrator re-pointed
    /// the definition at a different calendar - and so the SLA maths never depends on a
    /// navigation happening to be loaded.
    /// </para>
    /// </summary>
    public Guid? BusinessCalendarId { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>The committed deadline, in UTC, computed against the business calendar.</summary>
    public DateTimeOffset DueAt { get; set; }

    /// <summary>Set when the target was achieved (first response recorded, incident resolved).</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Set the moment the clock was first found to be past <see cref="DueAt"/>.</summary>
    public DateTimeOffset? BreachedAt { get; set; }

    /// <summary>Set when a warning notification has been raised, so it is not sent repeatedly.</summary>
    public DateTimeOffset? WarnedAt { get; set; }

    // --- Pause tracking ---
    /// <summary>Non-null while the clock is stopped.</summary>
    public DateTimeOffset? PausedAt { get; set; }

    /// <summary>Accumulated business minutes spent paused, already reflected in <see cref="DueAt"/>.</summary>
    public int PausedBusinessMinutes { get; set; }

    public SlaState State { get; set; } = SlaState.InProgress;

    public SlaDefinition? SlaDefinition { get; set; }

    /// <summary>True once the outcome is decided and the clock no longer needs monitoring.</summary>
    public bool IsSettled => State is SlaState.Met or SlaState.Breached or SlaState.Cancelled;

    /// <summary>
    /// Business minutes consumed so far. For a settled clock this is measured to completion;
    /// for a paused clock, to the moment it paused; otherwise to <paramref name="now"/>.
    /// </summary>
    public int ElapsedBusinessMinutes(BusinessSchedule schedule, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var endpoint = CompletedAt ?? PausedAt ?? now;
        var elapsed = schedule.BusinessMinutesBetween(StartedAt, endpoint);
        return Math.Max(0, elapsed - PausedBusinessMinutes);
    }

    /// <summary>
    /// Business minutes left before the deadline. Negative once the commitment is overrun,
    /// which lets the UI show "2h 15m over" as naturally as "2h 15m left".
    /// </summary>
    public int RemainingBusinessMinutes(BusinessSchedule schedule, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var reference = CompletedAt ?? PausedAt ?? now;
        return reference <= DueAt
            ? schedule.BusinessMinutesBetween(reference, DueAt)
            : -schedule.BusinessMinutesBetween(DueAt, reference);
    }

    /// <summary>Consumption as a percentage of the commitment, for the progress meter.</summary>
    public int ConsumedPercent(BusinessSchedule schedule, DateTimeOffset now)
    {
        if (DurationMinutes <= 0)
        {
            return 0;
        }

        var percent = (int)Math.Round(
            ElapsedBusinessMinutes(schedule, now) * 100.0 / DurationMinutes,
            MidpointRounding.AwayFromZero);

        return Math.Clamp(percent, 0, 999);
    }

    /// <summary>Stops the clock. Called when the record enters a pending state.</summary>
    public void Pause(DateTimeOffset now)
    {
        if (State != SlaState.InProgress)
        {
            return;
        }

        PausedAt = now;
        State = SlaState.Paused;
    }

    /// <summary>
    /// Restarts the clock and pushes the deadline out by the working time spent paused, so the
    /// customer is not charged for time the service desk was blocked.
    /// </summary>
    public void Resume(BusinessSchedule schedule, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (State != SlaState.Paused || PausedAt is null)
        {
            return;
        }

        var pausedMinutes = schedule.BusinessMinutesBetween(PausedAt.Value, now);
        if (pausedMinutes > 0)
        {
            PausedBusinessMinutes += pausedMinutes;
            DueAt = schedule.AddBusinessMinutes(DueAt, pausedMinutes);
        }

        PausedAt = null;

        // A clock that was already past its deadline when it paused stays breached.
        State = BreachedAt is not null ? SlaState.Breached : SlaState.InProgress;
    }

    /// <summary>
    /// Stops the clock because the target was achieved. Whether that counts as met or breached
    /// depends on the completion time against the deadline, not on when we noticed.
    /// </summary>
    public void Complete(DateTimeOffset completedAt)
    {
        if (IsSettled)
        {
            return;
        }

        CompletedAt = completedAt;
        PausedAt = null;

        if (completedAt > DueAt)
        {
            BreachedAt ??= DueAt;
            State = SlaState.Breached;
        }
        else
        {
            State = SlaState.Met;
        }
    }

    /// <summary>
    /// Marks the commitment overrun. Called by the SLA monitor when it observes a running clock
    /// past its deadline. The breach timestamp is the deadline itself, not the detection time,
    /// so reporting is not distorted by how often the monitor happens to run.
    /// <para>
    /// Only a running clock can breach. A paused clock is, by definition, not consuming the
    /// customer's allowance - the service desk is waiting on someone else - so it must not tip
    /// into breach while stopped. The deadline is pushed out by the paused working time when the
    /// clock resumes, and only then can it overrun.
    /// </para>
    /// </summary>
    public bool MarkBreachedIfOverdue(DateTimeOffset now)
    {
        if (State != SlaState.InProgress || now <= DueAt)
        {
            return false;
        }

        BreachedAt = DueAt;
        State = SlaState.Breached;
        return true;
    }

    /// <summary>Stops the clock without an outcome, e.g. when the incident is cancelled.</summary>
    public void Cancel()
    {
        if (IsSettled)
        {
            return;
        }

        PausedAt = null;
        State = SlaState.Cancelled;
    }
}
