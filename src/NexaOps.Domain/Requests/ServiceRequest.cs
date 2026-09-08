using NexaOps.Domain.Approvals;
using NexaOps.Domain.Common;
using NexaOps.Domain.Identity;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Sla;

namespace NexaOps.Domain.Requests;

/// <summary>
/// A request for something to be provided: access, equipment, software, a service.
/// <para>
/// This is an aggregate root. Status changes, approval outcomes and fulfilment go through the
/// methods below rather than raw property assignment, so the invariants hold whichever caller is
/// driving - REST, the approval flow, or a future workflow step.
/// </para>
/// <para>
/// Distinct from an incident by design: nothing is broken, the work is planned, and it may need
/// authorising before anyone starts.
/// </para>
/// </summary>
public class ServiceRequest : TenantEntity
{
    /// <summary>Human-facing identifier, e.g. REQ0001042. Unique per tenant, never reused.</summary>
    public string Number { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // --- People ---

    /// <summary>The person who submitted it.</summary>
    public Guid RequesterId { get; set; }

    /// <summary>
    /// The person it is for. Differs from the requester when a manager orders on behalf of a
    /// new starter, which is one of the most common request patterns in practice.
    /// </summary>
    public Guid RequestedForId { get; set; }

    public Guid? OrganizationId { get; set; }
    public Guid? DepartmentId { get; set; }

    // --- Classification ---
    public Guid? CategoryId { get; set; }
    public Priority Priority { get; set; } = Priority.P4Low;

    // --- Ownership ---
    public Guid? FulfilmentGroupId { get; set; }
    public Guid? AssignedToUserId { get; set; }

    // --- Lifecycle ---
    public RequestStatus Status { get; set; } = RequestStatus.Draft;
    public RequestPendingReason? PendingReason { get; set; }
    public RequestChannel Channel { get; set; } = RequestChannel.Portal;

    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
    public Guid? FulfilledByUserId { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosedByUserId { get; set; }

    /// <summary>Set when an approver refuses. Mandatory on rejection, so the requester is told why.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>Free-text note recorded when the request is cancelled.</summary>
    public string? CancellationReason { get; set; }

    /// <summary>Date the requester needs it by. Advisory - it does not drive the SLA.</summary>
    public DateOnly? RequiredByDate { get; set; }

    // --- Denormalised roll-ups, so list views need no extra queries ---
    public bool HasBreachedSla { get; set; }
    public DateTimeOffset? NextSlaDueAt { get; set; }

    /// <summary>Total indicative cost of the lines, captured when the request was submitted.</summary>
    public decimal? TotalCost { get; set; }

    // --- Navigation ---
    public User? Requester { get; set; }
    public User? RequestedFor { get; set; }
    public User? AssignedTo { get; set; }
    public Group? FulfilmentGroup { get; set; }
    public Category? Category { get; set; }

    public ICollection<RequestItem> Items { get; set; } = new List<RequestItem>();
    public ICollection<Approval> Approvals { get; set; } = new List<Approval>();
    public ICollection<RequestComment> Comments { get; set; } = new List<RequestComment>();
    public ICollection<SlaInstance> SlaInstances { get; set; } = new List<SlaInstance>();

    // ------------------------------------------------------------------
    // Behaviour
    // ------------------------------------------------------------------

    /// <summary>
    /// Moves the request to a new status, enforcing the state machine and capturing the
    /// timestamps that cycle-time reporting depends on.
    /// </summary>
    public void TransitionTo(RequestStatus next, Guid actorUserId, DateTimeOffset now)
    {
        RequestStateMachine.EnsureCanTransition(Status, next);

        if (Status == next)
        {
            return;
        }

        Status = next;

        switch (next)
        {
            case RequestStatus.AwaitingApproval:
                SubmittedAt ??= now;
                break;

            case RequestStatus.Approved:
                SubmittedAt ??= now;
                ApprovedAt ??= now;
                break;

            case RequestStatus.Fulfilled:
                FulfilledAt = now;
                FulfilledByUserId = actorUserId;
                break;

            case RequestStatus.Closed:
                ClosedAt = now;
                ClosedByUserId = actorUserId;
                break;
        }

        // Leaving Pending clears the reason, so a stale "awaiting vendor" cannot linger on a
        // request that is actively being worked.
        if (next != RequestStatus.Pending)
        {
            PendingReason = null;
        }
    }

    /// <summary>
    /// Submits a draft. Routes to approval or straight to fulfilment depending on what the
    /// ordered items require.
    /// </summary>
    public void Submit(bool requiresApproval, Guid actorUserId, DateTimeOffset now)
    {
        if (Status != RequestStatus.Draft)
        {
            throw new DomainException(
                "request.not_draft",
                "Only a draft request can be submitted.");
        }

        if (Items.Count == 0)
        {
            throw new DomainException(
                "request.no_items",
                "A request must contain at least one item before it can be submitted.");
        }

        TransitionTo(
            requiresApproval ? RequestStatus.AwaitingApproval : RequestStatus.Approved,
            actorUserId,
            now);
    }

    /// <summary>
    /// Records that every approval stage cleared. Called by the approval service, never directly
    /// by a controller, so the decision arithmetic has exactly one home.
    /// </summary>
    public void RecordApproved(Guid actorUserId, DateTimeOffset now)
    {
        if (Status != RequestStatus.AwaitingApproval)
        {
            return;
        }

        TransitionTo(RequestStatus.Approved, actorUserId, now);
    }

    /// <summary>Records a refusal. The reason is mandatory - a rejection with no explanation is
    /// a support ticket waiting to happen.</summary>
    public void RecordRejected(string reason, Guid actorUserId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                "request.rejection_reason_required",
                "A reason is required when rejecting a request.");
        }

        RequestStateMachine.EnsureCanTransition(Status, RequestStatus.Rejected);

        RejectionReason = reason.Trim();
        Status = RequestStatus.Rejected;
        ClosedAt = now;
        ClosedByUserId = actorUserId;
    }

    /// <summary>Assigns fulfilment ownership.</summary>
    public void Assign(Guid? groupId, Guid? userId)
    {
        FulfilmentGroupId = groupId;
        AssignedToUserId = userId;
    }

    /// <summary>Puts the request on hold, requiring a reason so the delay is explainable.</summary>
    public void PutOnHold(RequestPendingReason reason, Guid actorUserId, DateTimeOffset now)
    {
        TransitionTo(RequestStatus.Pending, actorUserId, now);
        PendingReason = reason;
    }

    /// <summary>Cancels the request, recording why.</summary>
    public void Cancel(string? reason, Guid actorUserId, DateTimeOffset now)
    {
        RequestStateMachine.EnsureCanTransition(Status, RequestStatus.Cancelled);

        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Status = RequestStatus.Cancelled;
        ClosedAt = now;
        ClosedByUserId = actorUserId;

        foreach (var item in Items.Where(i => i.Status is RequestItemStatus.Pending or RequestItemStatus.InProgress))
        {
            item.Status = RequestItemStatus.Cancelled;
        }
    }

    /// <summary>
    /// True once every line has reached a settled state, which is what lets the request itself
    /// be marked fulfilled.
    /// </summary>
    public bool AllItemsSettled()
        => Items.Count > 0
           && Items.All(i => i.Status is RequestItemStatus.Fulfilled or RequestItemStatus.Cancelled);
}
