using NexaOps.Domain.Common;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Domain.Workflows;

/// <summary>
/// One automation rule: when this happens to this kind of record, and these things are true,
/// do these things.
/// <para>
/// The engine is deliberately a rule list rather than a flowchart. A flowchart designer is the
/// feature customers ask for and the feature nobody can debug at three in the morning; a short
/// ordered list of conditions and actions is readable in the shape it is stored in, which is
/// what makes a run history explainable afterwards.
/// </para>
/// </summary>
public sealed class WorkflowDefinition : TenantEntity
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Which module's records this rule watches.</summary>
    public ServiceModule Module { get; set; }

    public WorkflowTrigger Trigger { get; set; }

    /// <summary>
    /// Inactive rules are kept rather than deleted. A rule that fired for six months is part of
    /// the explanation of what happened to records in that period, and deleting it makes the run
    /// history unreadable.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Evaluation order among rules on the same trigger. Lower runs first. Rules are not
    /// mutually exclusive: every matching rule runs.
    /// </summary>
    public int Sequence { get; set; }

    /// <summary>
    /// Conditions are combined with AND, always.
    /// <para>
    /// There is no OR and no nesting. Both are easy to store and hard to read back, and a rule
    /// whose behaviour cannot be predicted by reading it is a rule that gets switched off after
    /// the first surprise. Two rules express an OR perfectly well.
    /// </para>
    /// </summary>
    public ICollection<WorkflowCondition> Conditions { get; } = [];

    public ICollection<WorkflowAction> Actions { get; } = [];

    /// <summary>Set on each run so the list can show which rules are actually doing anything.</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    public int RunCount { get; set; }

    /// <summary>
    /// Whether this rule can run at all. A rule with no actions is a rule that does nothing, and
    /// storing it active would put a row in the run history for every record that matched.
    /// </summary>
    public bool IsRunnable => IsActive && Actions.Count > 0;
}

/// <summary>One test a record must pass for its rule to run.</summary>
public sealed class WorkflowCondition : TenantEntity
{
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>
    /// The fact key, matched against <see cref="IWorkflowTarget.WorkflowFacts"/>. A key the
    /// module does not publish evaluates as absent rather than throwing: a rule written against
    /// a field that later stopped being published should stop matching, not break the record.
    /// </summary>
    public required string Field { get; set; }

    public WorkflowConditionOperator Operator { get; set; }

    /// <summary>
    /// The comparison value, as text. Comma-separated for <see cref="WorkflowConditionOperator.In"/>,
    /// and ignored for the emptiness operators.
    /// </summary>
    public string? Value { get; set; }
}

/// <summary>One thing a rule does when it matches.</summary>
public sealed class WorkflowAction : TenantEntity
{
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>Execution order within the rule. Lower runs first.</summary>
    public int Sequence { get; set; }

    public WorkflowActionType Type { get; set; }

    /// <summary>Who to notify, for the notification actions.</summary>
    public WorkflowRecipient? Recipient { get; set; }

    /// <summary>Named target for the group actions, and for <see cref="WorkflowRecipient.SpecificGroup"/>.</summary>
    public Guid? TargetGroupId { get; set; }

    /// <summary>Named target for the user actions, and for <see cref="WorkflowRecipient.SpecificUser"/>.</summary>
    public Guid? TargetUserId { get; set; }

    /// <summary>The priority to raise to, for <see cref="WorkflowActionType.SetPriority"/>.</summary>
    public Priority? TargetPriority { get; set; }

    /// <summary>
    /// The notification body. Supports two placeholders only — <c>{number}</c> and
    /// <c>{title}</c> — because a template language is a parser, and a parser that runs on
    /// tenant-supplied text is a security decision rather than a convenience.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>Renders the message for a record, or a plain default when none was configured.</summary>
    public string RenderMessage(string number, string title)
        => string.IsNullOrWhiteSpace(Message)
            ? title
            : Message.Replace("{number}", number, StringComparison.Ordinal)
                .Replace("{title}", title, StringComparison.Ordinal);
}
