using NexaOps.Domain.Common;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Domain.Workflows;

/// <summary>
/// One evaluation of one rule against one record.
/// <para>
/// Runs are recorded even when nothing happened. "Why did my rule not fire" is the question
/// people actually have about automation, and it is unanswerable from a log that only records
/// successes. A skipped run says which condition failed.
/// </para>
/// </summary>
public sealed class WorkflowRun : TenantEntity
{
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>
    /// Denormalised so the history stays readable after a rule is renamed. The run describes
    /// what happened at the time, not what the rule is called now.
    /// </summary>
    public required string WorkflowName { get; set; }

    public ServiceModule Module { get; set; }

    public Guid RecordId { get; set; }

    public required string RecordNumber { get; set; }

    public WorkflowTrigger Trigger { get; set; }

    public WorkflowRunStatus Status { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Why a run was skipped or suppressed, in a sentence somebody can act on.</summary>
    public string? Outcome { get; set; }

    public ICollection<WorkflowStepRun> Steps { get; } = [];

    public TimeSpan? Duration => CompletedAt is null ? null : CompletedAt - StartedAt;

    /// <summary>
    /// Settles the run from the steps that actually ran.
    /// <para>
    /// A run with a mix of outcomes is <see cref="WorkflowRunStatus.PartiallyCompleted"/> rather
    /// than either extreme, because both extremes would be a lie: the record did move, and
    /// something did not happen that was supposed to.
    /// </para>
    /// </summary>
    public void Complete(DateTimeOffset now)
    {
        CompletedAt = now;

        var attempted = Steps.Where(s => s.Status != WorkflowStepStatus.Skipped).ToList();

        // Every action skipped itself — an unassigned record with only assignee-facing actions.
        // Nothing failed, so this is a success with nothing to show for it.
        if (attempted.Count == 0)
        {
            Status = WorkflowRunStatus.Succeeded;
            Outcome ??= "Every action was skipped because the record had nothing to act on.";
            return;
        }

        var failed = attempted.Count(s => s.Status == WorkflowStepStatus.Failed);

        Status = failed switch
        {
            0 => WorkflowRunStatus.Succeeded,
            _ when failed == attempted.Count => WorkflowRunStatus.Failed,
            _ => WorkflowRunStatus.PartiallyCompleted
        };
    }
}

/// <summary>One action within a run, and what it did or did not do.</summary>
public sealed class WorkflowStepRun : TenantEntity
{
    public Guid WorkflowRunId { get; set; }

    public int Sequence { get; set; }

    public WorkflowActionType ActionType { get; set; }

    public WorkflowStepStatus Status { get; set; }

    /// <summary>What happened, in a sentence. "Notified 4 members of Network Operations."</summary>
    public string? Detail { get; set; }

    /// <summary>
    /// The failure, when there was one. Stored as a message rather than a stack trace: this is
    /// read by an administrator deciding whether their rule is wrong, not by a developer.
    /// </summary>
    public string? Error { get; set; }
}
