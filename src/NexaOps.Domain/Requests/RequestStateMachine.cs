using NexaOps.Domain.Common;

namespace NexaOps.Domain.Requests;

/// <summary>
/// The single source of truth for which service request transitions are legal.
/// <para>
/// Mirrors <c>IncidentStateMachine</c> in shape so the two modules behave predictably, but the
/// rules differ where the business differs: a request must pass through approval before work
/// starts, and a rejected request is terminal.
/// </para>
/// </summary>
public static class RequestStateMachine
{
    private static readonly Dictionary<RequestStatus, RequestStatus[]> Allowed = new()
    {
        // A draft has not been submitted. It can only go forward into the approval decision or
        // be abandoned.
        [RequestStatus.Draft] =
        [
            RequestStatus.AwaitingApproval, RequestStatus.Approved, RequestStatus.Cancelled
        ],

        [RequestStatus.AwaitingApproval] =
        [
            RequestStatus.Approved, RequestStatus.Rejected, RequestStatus.Cancelled
        ],

        [RequestStatus.Approved] =
        [
            RequestStatus.InProgress, RequestStatus.Pending, RequestStatus.Fulfilled,
            RequestStatus.Cancelled
        ],

        [RequestStatus.InProgress] =
        [
            RequestStatus.Pending, RequestStatus.Fulfilled, RequestStatus.Cancelled
        ],

        [RequestStatus.Pending] =
        [
            RequestStatus.InProgress, RequestStatus.Fulfilled, RequestStatus.Cancelled
        ],

        // Fulfilled work can be reopened while the requester confirms, exactly as a resolved
        // incident can. Closing ends it.
        [RequestStatus.Fulfilled] = [RequestStatus.Closed, RequestStatus.InProgress],

        // Terminal. A rejected request is not re-submitted: a new request is raised, which keeps
        // approval and cycle-time reporting honest.
        [RequestStatus.Closed] = [],
        [RequestStatus.Rejected] = [],
        [RequestStatus.Cancelled] = []
    };

    public static bool CanTransition(RequestStatus from, RequestStatus to)
        => from == to || (Allowed.TryGetValue(from, out var targets) && targets.Contains(to));

    public static IReadOnlyCollection<RequestStatus> AllowedTransitionsFrom(RequestStatus from)
        => Allowed.TryGetValue(from, out var targets) ? targets : [];

    /// <summary>Throws <see cref="DomainException"/> when the transition is not permitted.</summary>
    public static void EnsureCanTransition(RequestStatus from, RequestStatus to)
    {
        if (CanTransition(from, to))
        {
            return;
        }

        throw new DomainException(
            "request.invalid_transition",
            $"A service request cannot move from {from} to {to}.");
    }

    /// <summary>True once the request no longer consumes fulfilment capacity.</summary>
    public static bool IsTerminal(RequestStatus status)
        => status is RequestStatus.Closed or RequestStatus.Rejected or RequestStatus.Cancelled;

    /// <summary>True while the request is still counted as open work on dashboards.</summary>
    public static bool IsOpen(RequestStatus status)
        => status is RequestStatus.Draft or RequestStatus.AwaitingApproval or RequestStatus.Approved
            or RequestStatus.InProgress or RequestStatus.Pending;

    /// <summary>
    /// Statuses during which the fulfilment SLA does not run.
    /// <para>
    /// Approval time is excluded deliberately. The service desk cannot start work it has not
    /// been authorised to do, so counting a manager's slow approval against the desk's
    /// fulfilment commitment would measure the wrong team. Approval duration is worth reporting
    /// on separately - it is a real delay - but it is not a fulfilment breach.
    /// </para>
    /// </summary>
    public static bool PausesSla(RequestStatus status)
        => status is RequestStatus.Pending or RequestStatus.AwaitingApproval or RequestStatus.Draft;
}
