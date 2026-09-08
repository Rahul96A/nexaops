namespace NexaOps.Domain.Workflows;

/// <summary>
/// What causes a workflow to be considered.
/// <para>
/// A closed set, and a deliberately small one. Each value corresponds to a point where a module
/// service already has to stop and tell the platform something happened — the same points the
/// SLA engine is driven from. A trigger that no service actually raises would be a rule that
/// silently never runs, which is worse than a missing feature because it looks configured.
/// </para>
/// </summary>
public enum WorkflowTrigger
{
    /// <summary>A record has just been created.</summary>
    RecordCreated = 1,

    /// <summary>A record has moved between lifecycle states.</summary>
    StatusChanged = 2,

    /// <summary>A record's priority has changed, whether derived or overridden.</summary>
    PriorityChanged = 3,

    /// <summary>A record's assignment group or assignee has changed.</summary>
    AssignmentChanged = 4
}

/// <summary>
/// What a workflow can do when its conditions match.
/// <para>
/// Every value here is backed by a service that already exists and is already tested. There is
/// no "run a script" and no "call a webhook": both would be a way to make the engine look more
/// capable than it is, and both need a security model — sandboxing, egress control, secret
/// handling — that has not been built.
/// </para>
/// </summary>
public enum WorkflowActionType
{
    /// <summary>Notify one person, resolved from the record or named outright.</summary>
    NotifyUser = 1,

    /// <summary>Notify every member of a group.</summary>
    NotifyGroup = 2,

    /// <summary>Route the record to an assignment group.</summary>
    AssignToGroup = 3,

    /// <summary>Assign the record to one person.</summary>
    AssignToUser = 4,

    /// <summary>Raise the record's priority. Lowering is refused — see the action.</summary>
    SetPriority = 5,

    /// <summary>Raise an approval against the record, using the module-agnostic approval engine.</summary>
    RequestApproval = 6
}

/// <summary>Who a notification action is aimed at.</summary>
public enum WorkflowRecipient
{
    /// <summary>Whoever raised the record.</summary>
    Requester = 1,

    /// <summary>Whoever currently holds it. Skipped, with a reason, when nobody does.</summary>
    Assignee = 2,

    /// <summary>Every member of the record's current assignment group.</summary>
    AssignmentGroup = 3,

    /// <summary>A specific person, named on the action.</summary>
    SpecificUser = 4,

    /// <summary>Every member of a specific group, named on the action.</summary>
    SpecificGroup = 5
}

/// <summary>How a condition compares a record's value against the configured one.</summary>
public enum WorkflowConditionOperator
{
    Equals = 1,
    NotEquals = 2,

    /// <summary>Matches any of a comma-separated list.</summary>
    In = 3,

    /// <summary>
    /// Numerically greater. Note that priority is numerically inverted — P1 is 1 — so a rule
    /// about "more urgent than" uses <see cref="LessThan"/>. The UI says so where it matters.
    /// </summary>
    GreaterThan = 4,

    LessThan = 5,

    /// <summary>The record has no value for this field.</summary>
    IsEmpty = 6,

    IsNotEmpty = 7,

    /// <summary>Case-insensitive substring match, for text fields.</summary>
    Contains = 8
}

/// <summary>How a workflow run ended.</summary>
public enum WorkflowRunStatus
{
    /// <summary>Conditions did not match. Recorded, so "why did my rule not fire" is answerable.</summary>
    Skipped = 1,

    /// <summary>Every action completed.</summary>
    Succeeded = 2,

    /// <summary>Some actions completed and some failed. The record still moved.</summary>
    PartiallyCompleted = 3,

    /// <summary>Every action failed.</summary>
    Failed = 4,

    /// <summary>
    /// Refused before running because it was triggered by another workflow's action and would
    /// have recursed. See the engine's re-entrancy guard.
    /// </summary>
    Suppressed = 5
}

/// <summary>How one action within a run ended.</summary>
public enum WorkflowStepStatus
{
    Succeeded = 1,

    /// <summary>Nothing to do — an unassigned record with a "notify the assignee" action.</summary>
    Skipped = 2,

    Failed = 3
}
