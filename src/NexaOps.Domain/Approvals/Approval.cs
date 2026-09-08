using NexaOps.Domain.Common;
using NexaOps.Domain.Identity;

namespace NexaOps.Domain.Approvals;

/// <summary>
/// One person's - or one group's - authorisation decision on a record.
/// <para>
/// Deliberately module-agnostic. It carries <see cref="Module"/> and <see cref="RecordId"/>
/// rather than a foreign key to a service request, so change management and other later modules
/// reuse this table and the decision arithmetic in <see cref="ApprovalStateMachine"/> without a
/// second implementation.
/// </para>
/// </summary>
public class Approval : TenantEntity
{
    /// <summary>Owning module, e.g. <c>Request</c>. Pairs with <see cref="RecordId"/>.</summary>
    public string Module { get; set; } = string.Empty;

    public Guid RecordId { get; set; }

    /// <summary>
    /// Sequential stage. Stage 1 must clear before stage 2 is raised, which is what supports
    /// "manager, then finance" without any workflow engine existing yet.
    /// </summary>
    public int Stage { get; set; } = 1;

    /// <summary>How approvals within this stage combine.</summary>
    public ApprovalRule Rule { get; set; } = ApprovalRule.AnyOne;

    public ApprovalTargetKind TargetKind { get; set; } = ApprovalTargetKind.User;

    /// <summary>The individual who must decide, when targeted at a user or a resolved manager.</summary>
    public Guid? ApproverUserId { get; set; }

    /// <summary>The group whose members may decide, when targeted at a group.</summary>
    public Guid? ApproverGroupId { get; set; }

    public ApprovalState State { get; set; } = ApprovalState.Pending;

    /// <summary>Who actually decided. For a group approval this is the member who acted.</summary>
    public Guid? DecidedByUserId { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>The approver's note. Mandatory on rejection, optional on approval.</summary>
    public string? Comment { get; set; }

    /// <summary>Short label describing what is being approved, so an approver's queue is readable.</summary>
    public string RecordLabel { get; set; } = string.Empty;

    // --- Navigation ---
    public User? ApproverUser { get; set; }
    public Group? ApproverGroup { get; set; }

    /// <summary>True while this approval is still waiting on somebody.</summary>
    public bool IsOutstanding => State == ApprovalState.Pending;

    /// <summary>
    /// Records an approval.
    /// </summary>
    public void Approve(Guid actorUserId, DateTimeOffset now, string? comment)
    {
        ApprovalStateMachine.EnsureCanDecide(State);

        State = ApprovalState.Approved;
        DecidedByUserId = actorUserId;
        DecidedAt = now;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }

    /// <summary>
    /// Records a refusal. The comment is mandatory: an unexplained rejection sends the requester
    /// straight to the service desk to ask why, which costs more than requiring one sentence.
    /// </summary>
    public void Reject(Guid actorUserId, DateTimeOffset now, string comment)
    {
        ApprovalStateMachine.EnsureCanDecide(State);

        if (string.IsNullOrWhiteSpace(comment))
        {
            throw new DomainException(
                "approval.reason_required",
                "A reason is required when rejecting.");
        }

        State = ApprovalState.Rejected;
        DecidedByUserId = actorUserId;
        DecidedAt = now;
        Comment = comment.Trim();
    }

    /// <summary>
    /// Withdraws an outstanding approval because the underlying record was cancelled. A settled
    /// approval is left exactly as it was - the record of what somebody decided is not rewritten
    /// because the request later went away.
    /// </summary>
    public void CancelIfOutstanding(DateTimeOffset now)
    {
        if (State != ApprovalState.Pending)
        {
            return;
        }

        State = ApprovalState.Cancelled;
        DecidedAt = now;
    }

    /// <summary>
    /// Marks the approval as no longer needed because a peer in the same AnyOne stage already
    /// decided. Distinct from Cancelled, which means the whole record went away.
    /// </summary>
    public void MarkNotRequired(DateTimeOffset now)
    {
        if (State != ApprovalState.Pending)
        {
            return;
        }

        State = ApprovalState.NotRequired;
        DecidedAt = now;
    }
}
