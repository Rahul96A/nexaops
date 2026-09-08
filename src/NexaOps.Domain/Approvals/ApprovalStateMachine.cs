using NexaOps.Domain.Common;

namespace NexaOps.Domain.Approvals;

/// <summary>
/// Decides how a set of approvals in one stage combines into a stage outcome.
/// <para>
/// This is pure arithmetic over states, with no database and no notion of who is calling, which
/// is what makes the rule directly testable and identical whether a decision arrives from the
/// REST API, an email reply, or a future workflow step.
/// </para>
/// </summary>
public static class ApprovalStateMachine
{
    /// <summary>What a stage as a whole has decided, once its individual approvals are counted.</summary>
    public enum StageOutcome
    {
        /// <summary>Still waiting on at least one approver.</summary>
        Pending = 1,
        Approved = 2,
        Rejected = 3
    }

    /// <summary>
    /// True when an approval can still be acted on. A settled approval is never re-decided:
    /// changing a recorded decision would destroy the audit value of having recorded it.
    /// </summary>
    public static bool CanDecide(ApprovalState state) => state == ApprovalState.Pending;

    /// <summary>Throws <see cref="DomainException"/> when the approval has already settled.</summary>
    public static void EnsureCanDecide(ApprovalState state)
    {
        if (CanDecide(state))
        {
            return;
        }

        throw new DomainException(
            "approval.already_decided",
            $"This approval has already been {state.ToString().ToLowerInvariant()} and cannot be changed.");
    }

    /// <summary>
    /// Combines the approvals of a single stage into one outcome.
    /// </summary>
    /// <param name="states">Every approval belonging to the stage.</param>
    /// <param name="rule">How they combine.</param>
    public static StageOutcome Evaluate(IReadOnlyCollection<ApprovalState> states, ApprovalRule rule)
    {
        ArgumentNullException.ThrowIfNull(states);

        // A stage with no approvers is approved. This is reached when a catalogue item is
        // configured to need approval but every candidate approver resolved to nobody - failing
        // closed would strand the request with no one able to release it.
        var live = states.Where(s => s != ApprovalState.Cancelled && s != ApprovalState.NotRequired).ToList();
        if (live.Count == 0)
        {
            return StageOutcome.Approved;
        }

        // A rejection is decisive under both rules. Under Unanimous every approver must agree,
        // so one refusal ends it; under AnyOne the first decision settles the stage, and a
        // rejection reaching here is that first decision.
        if (live.Contains(ApprovalState.Rejected))
        {
            return StageOutcome.Rejected;
        }

        return rule switch
        {
            ApprovalRule.Unanimous => live.All(s => s == ApprovalState.Approved)
                ? StageOutcome.Approved
                : StageOutcome.Pending,

            ApprovalRule.AnyOne => live.Any(s => s == ApprovalState.Approved)
                ? StageOutcome.Approved
                : StageOutcome.Pending,

            _ => StageOutcome.Pending
        };
    }
}
