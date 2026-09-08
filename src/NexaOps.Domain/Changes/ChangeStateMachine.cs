using NexaOps.Domain.Common;

namespace NexaOps.Domain.Changes;

/// <summary>
/// The single source of truth for which change transitions are legal.
/// <para>
/// The rule that matters most is that <see cref="ChangeStatus.Review"/> is not optional. A
/// change is not finished when the work stops; it is finished when somebody has confirmed it did
/// what it claimed. Allowing Implementing to jump to Closed would make the outcome field - and
/// therefore change success reporting - meaningless.
/// </para>
/// </summary>
public static class ChangeStateMachine
{
    private static readonly Dictionary<ChangeStatus, ChangeStatus[]> Allowed = new()
    {
        [ChangeStatus.Draft] =
        [
            ChangeStatus.Assessing, ChangeStatus.AwaitingApproval, ChangeStatus.Scheduled,
            ChangeStatus.Cancelled
        ],

        [ChangeStatus.Assessing] =
        [
            ChangeStatus.AwaitingApproval, ChangeStatus.Scheduled, ChangeStatus.Draft,
            ChangeStatus.Cancelled
        ],

        [ChangeStatus.AwaitingApproval] =
        [
            ChangeStatus.Scheduled, ChangeStatus.Rejected, ChangeStatus.Assessing,
            ChangeStatus.Cancelled
        ],

        [ChangeStatus.Scheduled] =
        [
            ChangeStatus.Implementing, ChangeStatus.Assessing, ChangeStatus.Cancelled
        ],

        // No path to Closed. Every implemented change goes through review, so the outcome is
        // always recorded by somebody rather than inferred from silence.
        [ChangeStatus.Implementing] = [ChangeStatus.Review, ChangeStatus.Cancelled],

        [ChangeStatus.Review] = [ChangeStatus.Closed],

        [ChangeStatus.Closed] = [],
        [ChangeStatus.Rejected] = [],
        [ChangeStatus.Cancelled] = []
    };

    public static bool CanTransition(ChangeStatus from, ChangeStatus to)
        => from == to || (Allowed.TryGetValue(from, out var targets) && targets.Contains(to));

    public static IReadOnlyCollection<ChangeStatus> AllowedTransitionsFrom(ChangeStatus from)
        => Allowed.TryGetValue(from, out var targets) ? targets : [];

    /// <summary>Throws <see cref="DomainException"/> when the transition is not permitted.</summary>
    public static void EnsureCanTransition(ChangeStatus from, ChangeStatus to)
    {
        if (CanTransition(from, to))
        {
            return;
        }

        throw new DomainException(
            "change.invalid_transition",
            $"A change cannot move from {from} to {to}.");
    }

    public static bool IsTerminal(ChangeStatus status)
        => status is ChangeStatus.Closed or ChangeStatus.Rejected or ChangeStatus.Cancelled;

    public static bool IsOpen(ChangeStatus status)
        => status is ChangeStatus.Draft or ChangeStatus.Assessing or ChangeStatus.AwaitingApproval
            or ChangeStatus.Scheduled or ChangeStatus.Implementing or ChangeStatus.Review;

    /// <summary>True once the change has been authorised to proceed.</summary>
    public static bool IsAuthorised(ChangeStatus status)
        => status is ChangeStatus.Scheduled or ChangeStatus.Implementing
            or ChangeStatus.Review or ChangeStatus.Closed;

    /// <summary>
    /// Whether a change of this type needs approval before it can be scheduled.
    /// <para>
    /// A standard change was approved once, when the procedure itself was accepted, so asking
    /// again each time is theatre. An emergency change is approved retrospectively: the service
    /// cannot wait, but the decision must still be reviewed by someone afterwards.
    /// </para>
    /// </summary>
    public static bool RequiresApprovalBeforeScheduling(ChangeType type)
        => type == ChangeType.Normal;

    /// <summary>True when approval must be recorded after the fact rather than before.</summary>
    public static bool RequiresRetrospectiveApproval(ChangeType type)
        => type == ChangeType.Emergency;
}
