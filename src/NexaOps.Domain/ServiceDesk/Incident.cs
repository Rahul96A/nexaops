using System.Globalization;
using NexaOps.Domain.Common;
using NexaOps.Domain.Identity;
using NexaOps.Domain.Sla;
using NexaOps.Domain.Workflows;

namespace NexaOps.Domain.ServiceDesk;

/// <summary>
/// An unplanned interruption to a service, or a reduction in its quality.
/// <para>
/// This is an aggregate root: status changes, assignment and resolution go through the methods
/// below rather than through raw property assignment, so that invariants (legal transitions,
/// required resolution fields, first-response capture) hold no matter which caller - REST, the
/// workflow engine, or a confirmed AI proposal - is driving the change.
/// </para>
/// </summary>
public class Incident : TenantEntity, ISlaTracked, ISlaProgressFacts, IWorkflowTarget
{
    /// <summary>Human-facing identifier, e.g. INC0001042. Unique per tenant, never reused.</summary>
    public string Number { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // --- People ---
    /// <summary>The person who reported it.</summary>
    public Guid RequesterId { get; set; }

    /// <summary>The person actually affected. Differs from the requester for proxy-raised tickets.</summary>
    public Guid? AffectedUserId { get; set; }

    public Guid? OrganizationId { get; set; }
    public Guid? DepartmentId { get; set; }

    // --- Classification ---
    public Guid? CategoryId { get; set; }
    public Guid? SubcategoryId { get; set; }

    public Impact Impact { get; set; } = Impact.Moderate;
    public Urgency Urgency { get; set; } = Urgency.Medium;
    public Priority Priority { get; set; } = Priority.P3Moderate;

    /// <summary>
    /// Set when an agent deliberately departs from the impact/urgency matrix. The reason is
    /// mandatory, which keeps priority inflation visible in reporting instead of invisible.
    /// </summary>
    public bool IsPriorityOverridden { get; set; }
    public string? PriorityOverrideReason { get; set; }

    // --- Ownership ---
    public Guid? AssignmentGroupId { get; set; }
    public Guid? AssignedToUserId { get; set; }

    // --- Lifecycle ---
    public IncidentStatus Status { get; set; } = IncidentStatus.New;
    public PendingReason? PendingReason { get; set; }
    public IncidentChannel Channel { get; set; } = IncidentChannel.Portal;

    public ResolutionCode? ResolutionCode { get; set; }
    public string? ResolutionNotes { get; set; }

    /// <summary>Timestamp of the first public comment by an agent. Drives the response SLA.</summary>
    public DateTimeOffset? FirstRespondedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosedByUserId { get; set; }

    /// <summary>How many times this incident has been reopened. A quality signal for reporting.</summary>
    public int ReopenCount { get; set; }

    // --- Relationships ---
    /// <summary>Parent incident when this one is a child of a wider outage.</summary>
    public Guid? ParentIncidentId { get; set; }

    /// <summary>Set once a problem record has been raised to find the root cause.</summary>
    public Guid? ProblemId { get; set; }

    /// <summary>Configuration item believed to be at fault. Populated in the CMDB phase.</summary>
    public Guid? ConfigurationItemId { get; set; }

    /// <summary>Flags a declared major incident, which drives escalation and comms.</summary>
    public bool IsMajorIncident { get; set; }

    /// <summary>Denormalised roll-up so list views can badge SLA state without N extra queries.</summary>
    public bool HasBreachedSla { get; set; }

    /// <summary>Earliest outstanding SLA due time across this incident. Used for queue ordering.</summary>
    public DateTimeOffset? NextSlaDueAt { get; set; }

    // --- SLA engine contract (ISlaTracked) ---

    /// <summary>Incidents own the Incident module's policies.</summary>
    public ServiceModule SlaModule => ServiceModule.Incident;

    /// <summary>Incidents classify to a subcategory; other modules may not.</summary>
    public Guid? SlaSubcategoryId => SubcategoryId;

    /// <summary>The assignment group is the owning group for policy matching.</summary>
    public Guid? SlaGroupId => AssignmentGroupId;

    /// <summary>Resolution settles the resolution commitment; closure does not restate it.</summary>
    public DateTimeOffset? SlaCompletedAt => ResolvedAt;

    // --- Workflow engine contract (IWorkflowTarget) ---

    /// <summary>Incidents are matched by rules written against the Incident module.</summary>
    public ServiceModule WorkflowModule => ServiceModule.Incident;

    /// <summary>The reporter, which is who customer-facing rules notify.</summary>
    public Guid? WorkflowRequesterId => RequesterId;

    /// <inheritdoc />
    public string WorkflowActionUrl => $"/incidents/{Id}";

    /// <summary>
    /// What a rule may test an incident on.
    /// <para>
    /// An explicit list rather than reflection over the entity: this is the module's public
    /// contract with the rule engine, and it should change only when somebody decides it should.
    /// Identifiers are published alongside names so a rule can be written either way — names
    /// read better and identifiers survive a rename.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, string?> WorkflowFacts => new Dictionary<string, string?>(
        StringComparer.OrdinalIgnoreCase)
    {
        ["Status"] = Status.ToString(),
        ["Priority"] = ((int)Priority).ToString(CultureInfo.InvariantCulture),
        ["Impact"] = ((int)Impact).ToString(CultureInfo.InvariantCulture),
        ["Urgency"] = ((int)Urgency).ToString(CultureInfo.InvariantCulture),
        ["Channel"] = Channel.ToString(),
        ["Title"] = Title,
        ["CategoryId"] = CategoryId?.ToString(),
        ["CategoryName"] = Category?.Name,
        ["SubcategoryId"] = SubcategoryId?.ToString(),
        ["AssignmentGroupId"] = AssignmentGroupId?.ToString(),
        ["AssignmentGroupName"] = AssignmentGroup?.Name,
        ["AssignedToUserId"] = AssignedToUserId?.ToString(),
        ["RequesterId"] = RequesterId.ToString(),
        ["OrganizationId"] = OrganizationId?.ToString(),
        ["DepartmentId"] = DepartmentId?.ToString(),
        ["IsMajorIncident"] = IsMajorIncident ? "true" : "false",
        ["HasBreachedSla"] = HasBreachedSla ? "true" : "false",
        ["IsPriorityOverridden"] = IsPriorityOverridden ? "true" : "false",
        ["ConfigurationItemId"] = ConfigurationItemId?.ToString(),
        ["ReopenCount"] = ReopenCount.ToString(CultureInfo.InvariantCulture)
    };

    /// <summary>
    /// Raises the priority on a rule's instruction, and refuses to lower it.
    /// <para>
    /// A rule that can de-prioritise is a rule that can quietly bury somebody's outage. Raising
    /// is recoverable — a person can always put it back — and it is the direction every real
    /// escalation rule needs. Priority set this way is marked as an override with the reason
    /// stated, so it stays visible in reporting rather than looking like the matrix decided it.
    /// </para>
    /// </summary>
    public void ApplyWorkflowPriority(Priority priority)
    {
        if ((int)priority >= (int)Priority)
        {
            throw new DomainException(
                "workflow.priority_not_raised",
                $"This incident is already {Priority}. A rule may raise priority but not lower it.");
        }

        OverridePriority(priority, "Raised automatically by a workflow rule.");
    }

    // --- Navigation ---
    public User? Requester { get; set; }
    public User? AffectedUser { get; set; }
    public User? AssignedTo { get; set; }
    public Group? AssignmentGroup { get; set; }
    public Category? Category { get; set; }
    public Subcategory? Subcategory { get; set; }
    public Incident? ParentIncident { get; set; }

    public ICollection<IncidentComment> Comments { get; set; } = new List<IncidentComment>();
    public ICollection<IncidentTag> Tags { get; set; } = new List<IncidentTag>();
    public ICollection<SlaInstance> SlaInstances { get; set; } = new List<SlaInstance>();

    // ------------------------------------------------------------------
    // Behaviour
    // ------------------------------------------------------------------

    /// <summary>
    /// Moves the incident to a new status, enforcing the state machine and capturing the
    /// timestamps that resolution- and closure-time reporting depends on.
    /// </summary>
    public void TransitionTo(IncidentStatus next, Guid actorUserId, DateTimeOffset now)
    {
        IncidentStateMachine.EnsureCanTransition(Status, next);

        if (Status == next)
        {
            return;
        }

        var isReopen = Status == IncidentStatus.Resolved && next == IncidentStatus.InProgress;

        if (next == IncidentStatus.Resolved)
        {
            if (ResolutionCode is null || string.IsNullOrWhiteSpace(ResolutionNotes))
            {
                throw new DomainException(
                    "incident.resolution_required",
                    "A resolution code and resolution notes are required before an incident can be resolved.");
            }

            ResolvedAt = now;
            ResolvedByUserId = actorUserId;
        }

        if (next == IncidentStatus.Closed)
        {
            // Guarded by the state machine as well; kept here so the rule is explicit
            // at the point where the closure timestamps are written.
            if (Status != IncidentStatus.Resolved)
            {
                throw new DomainException(
                    "incident.close_requires_resolution",
                    "An incident must be resolved before it can be closed.");
            }

            ClosedAt = now;
            ClosedByUserId = actorUserId;
        }

        if (isReopen)
        {
            ReopenCount++;
            ResolvedAt = null;
            ResolvedByUserId = null;
            ResolutionCode = null;
            ResolutionNotes = null;
        }

        if (next != IncidentStatus.Pending)
        {
            PendingReason = null;
        }

        Status = next;
    }

    /// <summary>
    /// Assigns ownership. Assigning to a person automatically advances a brand-new incident to
    /// <see cref="IncidentStatus.Assigned"/>, which is what agents expect and removes a click.
    /// </summary>
    public void Assign(Guid? groupId, Guid? userId)
    {
        if (IncidentStateMachine.IsTerminal(Status))
        {
            throw new DomainException(
                "incident.assign_terminal",
                $"A {Status} incident cannot be reassigned.");
        }

        AssignmentGroupId = groupId;
        AssignedToUserId = userId;

        if (userId is not null && Status == IncidentStatus.New)
        {
            Status = IncidentStatus.Assigned;
        }
    }

    /// <summary>
    /// Records the first agent response. Idempotent: only the first call has an effect, because
    /// the response SLA measures time to first contact, not most recent contact.
    /// </summary>
    public void RecordFirstResponse(DateTimeOffset now)
        => FirstRespondedAt ??= now;

    /// <summary>Applies a matrix-derived priority unless an agent has explicitly overridden it.</summary>
    public void ApplyDerivedPriority(Priority derived)
    {
        if (!IsPriorityOverridden)
        {
            Priority = derived;
        }
    }

    /// <summary>Records a deliberate departure from the priority matrix. A reason is mandatory.</summary>
    public void OverridePriority(Priority priority, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                "incident.override_reason_required",
                "A reason is required when overriding the calculated priority.");
        }

        Priority = priority;
        IsPriorityOverridden = true;
        PriorityOverrideReason = reason.Trim();
    }
}
