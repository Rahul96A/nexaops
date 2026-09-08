using System.Globalization;
using NexaOps.Domain.Common;
using NexaOps.Domain.Identity;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Workflows;

namespace NexaOps.Domain.Changes;

/// <summary>
/// A planned modification to a service or its supporting infrastructure.
/// <para>
/// An aggregate root. The module exists to make the trade-off between speed and safety explicit:
/// a standard change proceeds immediately because its procedure was approved once, a normal
/// change is assessed and authorised first, and an emergency change proceeds now and is reviewed
/// afterwards. Recording which of the three applied is what makes the process auditable.
/// </para>
/// </summary>
public class Change : TenantEntity, IWorkflowTarget
{
    /// <summary>Human-facing identifier, e.g. CHG0000042. Unique per tenant, never reused.</summary>
    public string Number { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public ChangeType Type { get; set; } = ChangeType.Normal;
    public ChangeStatus Status { get; set; } = ChangeStatus.Draft;

    // --- Assessment ---
    public ChangeRisk Risk { get; set; } = ChangeRisk.Medium;
    public Impact Impact { get; set; } = Impact.Moderate;
    public Priority Priority { get; set; } = Priority.P3Moderate;

    /// <summary>What will be done. Required before a change can be approved or scheduled.</summary>
    public string? ImplementationPlan { get; set; }

    /// <summary>
    /// How to undo it. Required for anything above low risk: a change nobody knows how to back
    /// out is a change that turns a bad hour into a bad week.
    /// </summary>
    public string? RollbackPlan { get; set; }

    /// <summary>How success will be confirmed. Required before the change can be closed.</summary>
    public string? TestPlan { get; set; }

    /// <summary>Who is affected and how, in business terms.</summary>
    public string? ImpactAssessment { get; set; }

    // --- Scheduling ---
    public DateTimeOffset? PlannedStartAt { get; set; }
    public DateTimeOffset? PlannedEndAt { get; set; }
    public DateTimeOffset? ActualStartAt { get; set; }
    public DateTimeOffset? ActualEndAt { get; set; }

    /// <summary>True when the change needs a service outage during its window.</summary>
    public bool RequiresDowntime { get; set; }

    // --- Ownership ---
    public Guid? AssignmentGroupId { get; set; }
    public Guid? AssignedToUserId { get; set; }

    /// <summary>The person accountable for the change succeeding.</summary>
    public Guid RequestedByUserId { get; set; }

    public Guid? CategoryId { get; set; }

    // --- Outcome ---
    public ChangeOutcome? Outcome { get; set; }

    /// <summary>What actually happened. Required at review, whatever the outcome.</summary>
    public string? ReviewNotes { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosedByUserId { get; set; }

    public string? RejectionReason { get; set; }
    public string? CancellationReason { get; set; }

    // --- Relationships ---

    /// <summary>The problem whose permanent fix this change delivers.</summary>
    public Guid? ProblemId { get; set; }

    /// <summary>Configuration items affected. Populated in the CMDB phase.</summary>
    public Guid? ConfigurationItemId { get; set; }

    // --- Workflow engine contract (IWorkflowTarget) ---

    /// <summary>Changes are matched by rules written against the Change module.</summary>
    public ServiceModule WorkflowModule => ServiceModule.Change;

    /// <summary>The person accountable for the change succeeding.</summary>
    public Guid? WorkflowRequesterId => RequestedByUserId;

    /// <inheritdoc />
    public string WorkflowActionUrl => $"/changes/{Id}";

    /// <summary>What a rule may test a change on.</summary>
    public IReadOnlyDictionary<string, string?> WorkflowFacts => new Dictionary<string, string?>(
        StringComparer.OrdinalIgnoreCase)
    {
        ["Status"] = Status.ToString(),
        ["Priority"] = ((int)Priority).ToString(CultureInfo.InvariantCulture),
        ["Risk"] = Risk.ToString(),
        ["Impact"] = ((int)Impact).ToString(CultureInfo.InvariantCulture),
        ["Type"] = Type.ToString(),
        ["Title"] = Title,
        ["CategoryId"] = CategoryId?.ToString(),
        ["CategoryName"] = Category?.Name,
        ["AssignmentGroupId"] = AssignmentGroupId?.ToString(),
        ["AssignmentGroupName"] = AssignmentGroup?.Name,
        ["AssignedToUserId"] = AssignedToUserId?.ToString(),
        ["RequestedByUserId"] = RequestedByUserId.ToString(),
        ["RequiresDowntime"] = RequiresDowntime ? "true" : "false",
        ["ConfigurationItemId"] = ConfigurationItemId?.ToString()
    };

    /// <summary>Raises priority on a rule's instruction. Lowering is refused.</summary>
    public void ApplyWorkflowPriority(Priority priority)
    {
        if ((int)priority >= (int)Priority)
        {
            throw new DomainException(
                "workflow.priority_not_raised",
                $"This change is already {Priority}. A rule may raise priority but not lower it.");
        }

        Priority = priority;
    }

    // --- Navigation ---
    public User? AssignedTo { get; set; }
    public User? RequestedBy { get; set; }
    public Group? AssignmentGroup { get; set; }
    public Category? Category { get; set; }

    public ICollection<ChangeComment> Comments { get; set; } = new List<ChangeComment>();

    /// <summary>Planned duration, or null when the window is not yet set.</summary>
    public TimeSpan? PlannedDuration =>
        PlannedStartAt is not null && PlannedEndAt is not null
            ? PlannedEndAt - PlannedStartAt
            : null;

    /// <summary>True once the window has been booked.</summary>
    public bool IsScheduled => PlannedStartAt is not null && PlannedEndAt is not null;

    // ------------------------------------------------------------------
    // Behaviour
    // ------------------------------------------------------------------

    /// <summary>
    /// Moves the change to a new status, enforcing the state machine and the evidence each stage
    /// requires.
    /// </summary>
    public void TransitionTo(ChangeStatus next, Guid actorUserId, DateTimeOffset now)
    {
        ChangeStateMachine.EnsureCanTransition(Status, next);

        if (Status == next)
        {
            return;
        }

        if (next is ChangeStatus.AwaitingApproval or ChangeStatus.Scheduled)
        {
            EnsurePlannedProperly();
        }

        if (next == ChangeStatus.Scheduled && !IsScheduled)
        {
            throw new DomainException(
                "change.window_required",
                "Set a planned start and end before scheduling the change.");
        }

        if (next == ChangeStatus.Review && string.IsNullOrWhiteSpace(TestPlan))
        {
            throw new DomainException(
                "change.test_plan_required",
                "Record how success will be confirmed before moving the change to review.");
        }

        if (next == ChangeStatus.Closed && Outcome is null)
        {
            // Closing without an outcome would make change success reporting a count of records
            // rather than a measure of anything.
            throw new DomainException(
                "change.outcome_required",
                "Record the outcome of the change before closing it.");
        }

        Status = next;

        switch (next)
        {
            case ChangeStatus.Scheduled:
                ApprovedAt ??= now;
                break;

            case ChangeStatus.Implementing:
                ActualStartAt ??= now;
                break;

            case ChangeStatus.Review:
                ActualEndAt ??= now;
                break;

            case ChangeStatus.Closed:
                ClosedAt = now;
                ClosedByUserId = actorUserId;
                break;
        }
    }

    /// <summary>
    /// Confirms the change has been planned to the standard its risk demands.
    /// </summary>
    private void EnsurePlannedProperly()
    {
        if (string.IsNullOrWhiteSpace(ImplementationPlan))
        {
            throw new DomainException(
                "change.implementation_plan_required",
                "An implementation plan is required before the change can proceed.");
        }

        // A change nobody knows how to back out turns a bad hour into a bad week. Low-risk
        // changes are exempt because the ceremony would outweigh the exposure.
        if (Risk > ChangeRisk.Low && string.IsNullOrWhiteSpace(RollbackPlan))
        {
            throw new DomainException(
                "change.rollback_plan_required",
                $"A rollback plan is required for a {Risk} risk change.");
        }
    }

    /// <summary>Books the implementation window.</summary>
    public void Schedule(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new DomainException(
                "change.invalid_window",
                "The change window must end after it starts.");
        }

        PlannedStartAt = start;
        PlannedEndAt = end;
    }

    /// <summary>Records the post-implementation review.</summary>
    public void RecordReview(ChangeOutcome outcome, string notes, Guid actorUserId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            // A failed change with no explanation teaches nobody anything, and a successful one
            // with no note is indistinguishable from one that was never reviewed.
            throw new DomainException(
                "change.review_notes_required",
                "Record what actually happened when reviewing the change.");
        }

        Outcome = outcome;
        ReviewNotes = notes.Trim();
        ReviewedAt = now;
        ReviewedByUserId = actorUserId;
    }

    /// <summary>Records an approver's refusal. Terminal.</summary>
    public void RecordRejected(string reason, Guid actorUserId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                "change.rejection_reason_required",
                "A reason is required when rejecting a change.");
        }

        ChangeStateMachine.EnsureCanTransition(Status, ChangeStatus.Rejected);

        RejectionReason = reason.Trim();
        Status = ChangeStatus.Rejected;
        ClosedAt = now;
        ClosedByUserId = actorUserId;
    }

    /// <summary>Cancels the change, recording why.</summary>
    public void Cancel(string? reason, Guid actorUserId, DateTimeOffset now)
    {
        ChangeStateMachine.EnsureCanTransition(Status, ChangeStatus.Cancelled);

        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Status = ChangeStatus.Cancelled;
        ClosedAt = now;
        ClosedByUserId = actorUserId;
    }

    /// <summary>Assigns implementation ownership.</summary>
    public void Assign(Guid? groupId, Guid? assigneeId)
    {
        AssignmentGroupId = groupId;
        AssignedToUserId = assigneeId;
    }

    /// <summary>
    /// True when this change overlaps another's window. Used to warn about collisions rather
    /// than to prevent them - two changes in one window is sometimes exactly the intent.
    /// </summary>
    public bool OverlapsWindowOf(DateTimeOffset otherStart, DateTimeOffset otherEnd)
        => PlannedStartAt is not null
           && PlannedEndAt is not null
           && PlannedStartAt < otherEnd
           && otherStart < PlannedEndAt;
}

/// <summary>Correspondence and implementation notes on a change.</summary>
public class ChangeComment : TenantEntity
{
    public Guid ChangeId { get; set; }

    public IncidentCommentKind Kind { get; set; } = IncidentCommentKind.WorkNote;

    public string Body { get; set; } = string.Empty;

    public Guid AuthorId { get; set; }

    /// <summary>Author name captured at write time, so the trail reads correctly after a rename.</summary>
    public string AuthorDisplayName { get; set; } = string.Empty;

    public Change? Change { get; set; }
    public User? Author { get; set; }
}
