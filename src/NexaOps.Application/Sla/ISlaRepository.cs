using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Sla;

namespace NexaOps.Application.Sla;

/// <summary>Access to SLA configuration and business calendars for the ambient tenant.</summary>
public interface ISlaRepository
{
    /// <summary>
    /// Active policies for a module, ordered by <see cref="SlaPolicy.Order"/>, with their
    /// definitions loaded. Cached: policies are read on every incident change and edited rarely.
    /// </summary>
    Task<IReadOnlyList<SlaPolicy>> GetActivePoliciesAsync(
        ServiceModule module,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the working-time schedule for a calendar. Passing null returns the tenant default
    /// calendar; when no default exists the implementation returns a 24x7 schedule, which fails
    /// safe by making SLA targets tighter rather than unenforceable.
    /// </summary>
    Task<BusinessSchedule> GetScheduleAsync(
        Guid? businessCalendarId,
        CancellationToken cancellationToken = default);

    /// <summary>Live clocks attached to one record.</summary>
    Task<IReadOnlyList<SlaInstance>> GetInstancesForRecordAsync(
        ServiceModule module,
        Guid recordId,
        CancellationToken cancellationToken = default);
}
