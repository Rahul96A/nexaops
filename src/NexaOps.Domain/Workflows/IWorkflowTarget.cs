using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Domain.Workflows;

/// <summary>
/// What a record must expose to be driven by the workflow engine.
/// <para>
/// The same shape of contract as <c>ISlaTracked</c>, and for the same reason: the engine's rules
/// should be identical across modules while each module keeps its own vocabulary. An incident is
/// "In progress" and a change is "Implementing", and neither should have to become the other for
/// a rule to be written about it.
/// </para>
/// <para>
/// Note what is deliberately absent: there is no setter for arbitrary fields. A workflow can
/// route a record and raise its priority, both of which are reversible and visible. Letting a
/// rule write any field it liked would make the audit trail read as though a person had done it.
/// </para>
/// </summary>
public interface IWorkflowTarget
{
    Guid Id { get; }

    Guid TenantId { get; }

    /// <summary>The human reference, used in notifications and in the run history.</summary>
    string Number { get; }

    string Title { get; }

    ServiceModule WorkflowModule { get; }

    /// <summary>Who raised it. The default recipient for most customer-facing rules.</summary>
    Guid? WorkflowRequesterId { get; }

    Guid? AssignmentGroupId { get; set; }

    Guid? AssignedToUserId { get; set; }

    Priority Priority { get; }

    /// <summary>Relative in-app route for notification deep links.</summary>
    string WorkflowActionUrl { get; }

    /// <summary>
    /// The record's values as the condition evaluator sees them, keyed by field name.
    /// <para>
    /// Flattening to strings rather than reflecting over the entity is deliberate: it makes the
    /// set of fields a rule may test an explicit decision by each module, rather than whatever
    /// happens to be a public property this month.
    /// </para>
    /// </summary>
    IReadOnlyDictionary<string, string?> WorkflowFacts { get; }

    /// <summary>
    /// Raises the priority. Implementations refuse to lower it — see
    /// <see cref="WorkflowActionType.SetPriority"/>.
    /// </summary>
    void ApplyWorkflowPriority(Priority priority);
}
