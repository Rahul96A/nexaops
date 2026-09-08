using NexaOps.Domain.Common;

namespace NexaOps.Domain.Problems;

/// <summary>
/// The single source of truth for which problem transitions are legal.
/// <para>
/// The rule that differs most from the incident machine is that
/// <see cref="ProblemStatus.KnownError"/> is a resting state, not a waypoint. A problem with a
/// published workaround is delivering value even if nobody ever funds the permanent fix, and the
/// lifecycle says so rather than pushing every problem towards Resolved.
/// </para>
/// </summary>
public static class ProblemStateMachine
{
    private static readonly Dictionary<ProblemStatus, ProblemStatus[]> Allowed = new()
    {
        [ProblemStatus.New] =
        [
            ProblemStatus.Investigating, ProblemStatus.KnownError, ProblemStatus.Cancelled
        ],

        [ProblemStatus.Investigating] =
        [
            ProblemStatus.KnownError, ProblemStatus.FixInProgress, ProblemStatus.Resolved,
            ProblemStatus.Cancelled
        ],

        // A known error can sit here indefinitely with a workaround in place, go forward to a
        // permanent fix, or go back to investigation if the workaround stops working.
        [ProblemStatus.KnownError] =
        [
            ProblemStatus.Investigating, ProblemStatus.FixInProgress, ProblemStatus.Resolved,
            ProblemStatus.Cancelled
        ],

        [ProblemStatus.FixInProgress] =
        [
            ProblemStatus.Resolved, ProblemStatus.KnownError, ProblemStatus.Cancelled
        ],

        // Reopened when the cause turns out not to have been removed.
        [ProblemStatus.Resolved] = [ProblemStatus.Closed, ProblemStatus.Investigating],

        [ProblemStatus.Closed] = [],
        [ProblemStatus.Cancelled] = []
    };

    public static bool CanTransition(ProblemStatus from, ProblemStatus to)
        => from == to || (Allowed.TryGetValue(from, out var targets) && targets.Contains(to));

    public static IReadOnlyCollection<ProblemStatus> AllowedTransitionsFrom(ProblemStatus from)
        => Allowed.TryGetValue(from, out var targets) ? targets : [];

    /// <summary>Throws <see cref="DomainException"/> when the transition is not permitted.</summary>
    public static void EnsureCanTransition(ProblemStatus from, ProblemStatus to)
    {
        if (CanTransition(from, to))
        {
            return;
        }

        throw new DomainException(
            "problem.invalid_transition",
            $"A problem cannot move from {from} to {to}.");
    }

    public static bool IsTerminal(ProblemStatus status)
        => status is ProblemStatus.Closed or ProblemStatus.Cancelled;

    /// <summary>
    /// True while the problem is still open work.
    /// <para>
    /// <see cref="ProblemStatus.KnownError"/> counts as open. A published workaround is not a
    /// finished problem — the cause is still there, and reporting that treated it as closed
    /// would hide a backlog of permanent fixes nobody is funding.
    /// </para>
    /// </summary>
    public static bool IsOpen(ProblemStatus status)
        => status is ProblemStatus.New or ProblemStatus.Investigating
            or ProblemStatus.KnownError or ProblemStatus.FixInProgress;

    /// <summary>True once a workaround is available for the service desk to use.</summary>
    public static bool HasPublishedWorkaround(ProblemStatus status)
        => status is ProblemStatus.KnownError or ProblemStatus.FixInProgress;
}
