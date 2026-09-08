using NexaOps.Domain.Common;

namespace NexaOps.Domain.ServiceDesk;

/// <summary>
/// The single source of truth for which incident status transitions are legal.
/// <para>
/// Keeping this in the domain - rather than as scattered if-statements in the service layer -
/// means the REST API, the workflow engine, and any AI tool all obey exactly the same rules,
/// and the rules are directly unit-testable without a database.
/// </para>
/// </summary>
public static class IncidentStateMachine
{
    private static readonly Dictionary<IncidentStatus, IncidentStatus[]> Allowed = new()
    {
        [IncidentStatus.New] =
        [
            IncidentStatus.Assigned, IncidentStatus.InProgress, IncidentStatus.Pending,
            IncidentStatus.Resolved, IncidentStatus.Cancelled
        ],
        [IncidentStatus.Assigned] =
        [
            IncidentStatus.InProgress, IncidentStatus.Pending, IncidentStatus.Resolved,
            IncidentStatus.Assigned, IncidentStatus.Cancelled
        ],
        [IncidentStatus.InProgress] =
        [
            IncidentStatus.Pending, IncidentStatus.Resolved, IncidentStatus.Assigned,
            IncidentStatus.Cancelled
        ],
        [IncidentStatus.Pending] =
        [
            IncidentStatus.InProgress, IncidentStatus.Assigned, IncidentStatus.Resolved,
            IncidentStatus.Cancelled
        ],

        // A resolved incident may be reopened while the confirmation window is open.
        [IncidentStatus.Resolved] = [IncidentStatus.Closed, IncidentStatus.InProgress],

        // Closed and Cancelled are terminal. Reopening a closed incident is deliberately not
        // permitted: the correct action is to raise a new incident that references this one,
        // which keeps resolution-time and SLA reporting honest.
        [IncidentStatus.Closed] = [],
        [IncidentStatus.Cancelled] = []
    };

    public static bool CanTransition(IncidentStatus from, IncidentStatus to)
        => from == to || (Allowed.TryGetValue(from, out var targets) && targets.Contains(to));

    public static IReadOnlyCollection<IncidentStatus> AllowedTransitionsFrom(IncidentStatus from)
        => Allowed.TryGetValue(from, out var targets) ? targets : [];

    /// <summary>Throws <see cref="DomainException"/> when the transition is not permitted.</summary>
    public static void EnsureCanTransition(IncidentStatus from, IncidentStatus to)
    {
        if (CanTransition(from, to))
        {
            return;
        }

        throw new DomainException(
            "incident.invalid_transition",
            $"An incident cannot move from {from} to {to}.");
    }

    /// <summary>True once the incident no longer consumes agent capacity.</summary>
    public static bool IsTerminal(IncidentStatus status)
        => status is IncidentStatus.Closed or IncidentStatus.Cancelled;

    /// <summary>True while the incident is still counted as open work on dashboards.</summary>
    public static bool IsOpen(IncidentStatus status)
        => status is IncidentStatus.New or IncidentStatus.Assigned
            or IncidentStatus.InProgress or IncidentStatus.Pending;

    /// <summary>
    /// Statuses during which the resolution SLA clock does not run. Time spent waiting on the
    /// requester is not time the service desk is accountable for.
    /// </summary>
    public static bool PausesSla(IncidentStatus status) => status == IncidentStatus.Pending;
}
