using NexaOps.Application.Common;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Workflows;

namespace NexaOps.Application.Workflows;

public sealed record WorkflowConditionDto(
    Guid Id,
    string Field,
    WorkflowConditionOperator Operator,
    string? Value);

public sealed record WorkflowActionDto(
    Guid Id,
    int Sequence,
    WorkflowActionType Type,
    WorkflowRecipient? Recipient,
    Guid? TargetGroupId,
    string? TargetGroupName,
    Guid? TargetUserId,
    string? TargetUserName,
    Priority? TargetPriority,
    string? Message);

public sealed record WorkflowSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    ServiceModule Module,
    WorkflowTrigger Trigger,
    bool IsActive,
    int Sequence,
    int ConditionCount,
    int ActionCount,
    DateTimeOffset? LastRunAt,
    int RunCount,
    DateTimeOffset CreatedAt);

public sealed record WorkflowDetailDto(
    Guid Id,
    string Name,
    string? Description,
    ServiceModule Module,
    WorkflowTrigger Trigger,
    bool IsActive,
    int Sequence,
    IReadOnlyList<WorkflowConditionDto> Conditions,
    IReadOnlyList<WorkflowActionDto> Actions,
    DateTimeOffset? LastRunAt,
    int RunCount,
    DateTimeOffset CreatedAt,
    byte[]? RowVersion);

public sealed record WorkflowStepRunDto(
    int Sequence,
    WorkflowActionType ActionType,
    WorkflowStepStatus Status,
    string? Detail,
    string? Error);

public sealed record WorkflowRunDto(
    Guid Id,
    Guid WorkflowDefinitionId,
    string WorkflowName,
    ServiceModule Module,
    Guid RecordId,
    string RecordNumber,
    WorkflowTrigger Trigger,
    WorkflowRunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? Outcome,
    IReadOnlyList<WorkflowStepRunDto> Steps);

/// <summary>
/// The fields a rule may be written against, published so the UI can offer them rather than
/// asking an administrator to guess at key names.
/// </summary>
/// <param name="Field">The fact key.</param>
/// <param name="Label">What to call it on screen.</param>
/// <param name="Hint">Anything surprising about it — notably that priority is inverted.</param>
public sealed record WorkflowFieldDto(string Field, string Label, string? Hint);

public sealed class WorkflowQuery : PagedQuery
{
    /// <summary>Matches the rule name or description. Rules are few, so this is a plain contains.</summary>
    public string? Search { get; set; }

    public ServiceModule? Module { get; set; }
    public WorkflowTrigger? Trigger { get; set; }
    public bool? IsActive { get; set; }
}

public sealed class WorkflowRunQuery : PagedQuery
{
    public Guid? WorkflowDefinitionId { get; set; }
    public Guid? RecordId { get; set; }
    public WorkflowRunStatus? Status { get; set; }
}

public sealed class UpsertWorkflowConditionCommand
{
    public string Field { get; set; } = string.Empty;
    public WorkflowConditionOperator Operator { get; set; }
    public string? Value { get; set; }
}

public sealed class UpsertWorkflowActionCommand
{
    public int Sequence { get; set; }
    public WorkflowActionType Type { get; set; }
    public WorkflowRecipient? Recipient { get; set; }
    public Guid? TargetGroupId { get; set; }
    public Guid? TargetUserId { get; set; }
    public Priority? TargetPriority { get; set; }
    public string? Message { get; set; }
}

public sealed class UpsertWorkflowCommand
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ServiceModule Module { get; set; }
    public WorkflowTrigger Trigger { get; set; }
    public bool IsActive { get; set; } = true;
    public int Sequence { get; set; }

    public IList<UpsertWorkflowConditionCommand> Conditions { get; init; } = [];
    public IList<UpsertWorkflowActionCommand> Actions { get; init; } = [];

    public byte[]? RowVersion { get; set; }
}
