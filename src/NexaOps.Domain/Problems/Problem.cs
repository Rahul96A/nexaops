using System.Globalization;
using NexaOps.Domain.Common;
using NexaOps.Domain.Identity;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Workflows;

namespace NexaOps.Domain.Problems;

/// <summary>
/// The underlying cause of one or more incidents.
/// <para>
/// An aggregate root. The distinction from an incident is the point of the module: an incident
/// asks "how do we restore service now", a problem asks "why did this happen and how do we stop
/// it happening again". Conflating them is how organisations end up firefighting the same
/// outage every month.
/// </para>
/// <para>
/// Deliberately not SLA-tracked. Investigation is open-ended work whose value is in being done
/// properly, and a countdown against it would push teams to close problems rather than solve
/// them.
/// </para>
/// </summary>
public class Problem : TenantEntity, IWorkflowTarget
{
    /// <summary>Human-facing identifier, e.g. PRB0000042. Unique per tenant, never reused.</summary>
    public string Number { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // --- Classification ---
    public Guid? CategoryId { get; set; }
    public Guid? SubcategoryId { get; set; }

    /// <summary>
    /// Priority here means "how much is this worth fixing", driven by how many incidents it
    /// causes rather than by how urgent any one of them felt.
    /// </summary>
    public Priority Priority { get; set; } = Priority.P3Moderate;

    public ProblemOrigin Origin { get; set; } = ProblemOrigin.FromIncident;

    // --- Ownership ---
    public Guid? AssignmentGroupId { get; set; }
    public Guid? AssignedToUserId { get; set; }

    /// <summary>
    /// The person accountable for the investigation. Distinct from the assignee, who does the
    /// work: an unowned problem is one nobody is answerable for, which is how they go stale.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    // --- Lifecycle ---
    public ProblemStatus Status { get; set; } = ProblemStatus.New;

    public DateTimeOffset? InvestigationStartedAt { get; set; }
    public DateTimeOffset? KnownErrorAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosedByUserId { get; set; }

    // --- Investigation findings ---

    /// <summary>What actually caused it. Required before the problem can become a known error.</summary>
    public string? RootCause { get; set; }

    public RootCauseConfidence? RootCauseConfidence { get; set; }

    /// <summary>
    /// What the service desk should do in the meantime. This is the field that makes the module
    /// pay for itself, so publishing a known error requires it.
    /// </summary>
    public string? Workaround { get; set; }

    /// <summary>How the cause was permanently removed.</summary>
    public string? PermanentFix { get; set; }

    /// <summary>The change that delivers the permanent fix. Populated in the change phase.</summary>
    public Guid? ChangeId { get; set; }

    /// <summary>Configuration item believed to be at fault. Populated in the CMDB phase.</summary>
    public Guid? ConfigurationItemId { get; set; }

    /// <summary>
    /// Denormalised count of incidents linked to this problem, so a prioritisation view does not
    /// aggregate the relation table on every row.
    /// </summary>
    public int LinkedIncidentCount { get; set; }

    /// <summary>True when this problem is judged to affect a service broadly enough to publish.</summary>
    public bool IsMajorProblem { get; set; }

    // --- Workflow engine contract (IWorkflowTarget) ---

    /// <summary>Problems are matched by rules written against the Problem module.</summary>
    public ServiceModule WorkflowModule => ServiceModule.Problem;

    /// <summary>
    /// The owner, who is the person accountable for the investigation.
    /// <para>
    /// A problem has no requester: it is raised by the service desk about a pattern, not by a
    /// person about their own trouble. The accountable owner is the nearest honest answer to
    /// "who does a requester-facing rule notify", and rules that want the assignee can say so.
    /// </para>
    /// </summary>
    public Guid? WorkflowRequesterId => OwnerUserId;

    /// <inheritdoc />
    public string WorkflowActionUrl => $"/problems/{Id}";

    /// <summary>What a rule may test a problem on.</summary>
    public IReadOnlyDictionary<string, string?> WorkflowFacts => new Dictionary<string, string?>(
        StringComparer.OrdinalIgnoreCase)
    {
        ["Status"] = Status.ToString(),
        ["Priority"] = ((int)Priority).ToString(CultureInfo.InvariantCulture),
        ["Origin"] = Origin.ToString(),
        ["Title"] = Title,
        ["CategoryId"] = CategoryId?.ToString(),
        ["CategoryName"] = Category?.Name,
        ["SubcategoryId"] = SubcategoryId?.ToString(),
        ["AssignmentGroupId"] = AssignmentGroupId?.ToString(),
        ["AssignmentGroupName"] = AssignmentGroup?.Name,
        ["AssignedToUserId"] = AssignedToUserId?.ToString(),
        ["OwnerUserId"] = OwnerUserId?.ToString(),
        ["IsMajorProblem"] = IsMajorProblem ? "true" : "false"
    };

    /// <summary>Raises priority on a rule's instruction. Lowering is refused.</summary>
    public void ApplyWorkflowPriority(Priority priority)
    {
        if ((int)priority >= (int)Priority)
        {
            throw new DomainException(
                "workflow.priority_not_raised",
                $"This problem is already {Priority}. A rule may raise priority but not lower it.");
        }

        Priority = priority;
    }

    // --- Navigation ---
    public User? AssignedTo { get; set; }
    public User? Owner { get; set; }
    public Group? AssignmentGroup { get; set; }
    public Category? Category { get; set; }
    public Subcategory? Subcategory { get; set; }

    public ICollection<ProblemComment> Comments { get; set; } = new List<ProblemComment>();

    /// <summary>True once the service desk has something usable to tell affected callers.</summary>
    public bool HasWorkaround => !string.IsNullOrWhiteSpace(Workaround);

    // ------------------------------------------------------------------
    // Behaviour
    // ------------------------------------------------------------------

    /// <summary>
    /// Moves the problem to a new status, enforcing the state machine and the evidence each
    /// state requires.
    /// </summary>
    public void TransitionTo(ProblemStatus next, Guid actorUserId, DateTimeOffset now)
    {
        ProblemStateMachine.EnsureCanTransition(Status, next);

        if (Status == next)
        {
            return;
        }

        // A known error is a promise to the service desk that there is something they can do.
        // Publishing one without a workaround makes that promise falsely.
        if (next == ProblemStatus.KnownError && !HasWorkaround)
        {
            throw new DomainException(
                "problem.workaround_required",
                "A workaround is required before a problem can be published as a known error.");
        }

        if (next == ProblemStatus.KnownError && string.IsNullOrWhiteSpace(RootCause))
        {
            throw new DomainException(
                "problem.root_cause_required",
                "A root cause is required before a problem can be published as a known error.");
        }

        if (next == ProblemStatus.Resolved && string.IsNullOrWhiteSpace(PermanentFix))
        {
            throw new DomainException(
                "problem.permanent_fix_required",
                "Record how the cause was removed before resolving the problem.");
        }

        Status = next;

        switch (next)
        {
            case ProblemStatus.Investigating:
                InvestigationStartedAt ??= now;
                break;

            case ProblemStatus.KnownError:
                InvestigationStartedAt ??= now;
                KnownErrorAt ??= now;
                break;

            case ProblemStatus.Resolved:
                ResolvedAt = now;
                ResolvedByUserId = actorUserId;
                break;

            case ProblemStatus.Closed:
                ClosedAt = now;
                ClosedByUserId = actorUserId;
                break;

            case ProblemStatus.Cancelled:
                ClosedAt = now;
                ClosedByUserId = actorUserId;
                break;
        }
    }

    /// <summary>Records the investigation findings.</summary>
    public void RecordFindings(string? rootCause, RootCauseConfidence? confidence, string? workaround)
    {
        if (!string.IsNullOrWhiteSpace(rootCause))
        {
            RootCause = rootCause.Trim();
            RootCauseConfidence = confidence ?? Problems.RootCauseConfidence.Suspected;
        }

        if (!string.IsNullOrWhiteSpace(workaround))
        {
            Workaround = workaround.Trim();
        }
    }

    /// <summary>Assigns investigation ownership.</summary>
    public void Assign(Guid? groupId, Guid? assigneeId, Guid? ownerId)
    {
        AssignmentGroupId = groupId;
        AssignedToUserId = assigneeId;

        if (ownerId is not null)
        {
            OwnerUserId = ownerId;
        }
    }
}

/// <summary>
/// Correspondence and investigation notes on a problem.
/// <para>
/// Reuses <see cref="IncidentCommentKind"/> rather than declaring a parallel enum, because the
/// public/internal distinction is identical and two enums meaning the same thing is how work
/// notes end up leaking in one module and not another.
/// </para>
/// </summary>
public class ProblemComment : TenantEntity
{
    public Guid ProblemId { get; set; }

    public IncidentCommentKind Kind { get; set; } = IncidentCommentKind.WorkNote;

    public string Body { get; set; } = string.Empty;

    public Guid AuthorId { get; set; }

    /// <summary>Author name captured at write time, so the trail reads correctly after a rename.</summary>
    public string AuthorDisplayName { get; set; } = string.Empty;

    public Problem? Problem { get; set; }
    public User? Author { get; set; }
}
