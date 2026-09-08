using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Notifications;
using NexaOps.Application.Requests;
using NexaOps.Domain.Approvals;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Platform;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Workflows;

namespace NexaOps.Application.Workflows;

/// <summary>
/// What module services call when something has happened to a record.
/// <para>
/// Deliberately the same shape as the SLA engine's entry points, and called from the same places
/// in the same services. There is no event bus and no background dispatcher: an automation that
/// runs "eventually" is one nobody can explain the timing of, and an in-process call keeps the
/// rule's effect inside the same transaction the user is already waiting on.
/// </para>
/// </summary>
public interface IWorkflowEngine
{
    /// <summary>
    /// Evaluates every active rule for this record's module and trigger, and runs the ones that
    /// match. Never throws: see the implementation for why.
    /// </summary>
    Task RunAsync(
        IWorkflowTarget target,
        WorkflowTrigger trigger,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class WorkflowEngine : IWorkflowEngine
{
    private const string EntityType = nameof(WorkflowRun);

    private readonly IWorkflowRepository _workflows;
    private readonly IWorkflowReferenceRepository _reference;
    private readonly INotificationService _notifications;
    private readonly IApprovalService _approvals;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<WorkflowEngine> _logger;

    /// <summary>
    /// True while a rule's actions are executing.
    /// <para>
    /// The engine is registered per request, so this is per request. It is the recursion guard:
    /// an action that raises priority causes a priority change, which is itself a trigger, which
    /// would run rules, which could raise priority again. One level of automation is a feature;
    /// automation that triggers automation is a loop that takes a tenant's service desk down and
    /// is impossible to reason about from the rule list.
    /// </para>
    /// </summary>
    private bool _executing;

    public WorkflowEngine(
        IWorkflowRepository workflows,
        IWorkflowReferenceRepository reference,
        INotificationService notifications,
        IApprovalService approvals,
        IAuditService audit,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock,
        ILogger<WorkflowEngine> logger)
    {
        _workflows = workflows;
        _reference = reference;
        _notifications = notifications;
        _approvals = approvals;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RunAsync(
        IWorkflowTarget target,
        WorkflowTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (_executing)
        {
            // A rule's own action triggered this. Recorded rather than silently dropped, because
            // an administrator who wrote a rule that cannot run deserves to be told.
            await SuppressAsync(target, trigger, cancellationToken).ConfigureAwait(false);
            return;
        }

        IReadOnlyList<WorkflowDefinition> definitions;

        try
        {
            definitions = await _workflows
                .GetRunnableAsync(target.WorkflowModule, trigger, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A failure to read the rules must not fail the user's action. Somebody resolving an
            // incident should not be blocked because an automation table is unreadable.
            _logger.LogError(
                ex,
                "Could not load workflow rules for {Module}/{Trigger}; {Number} continued without automation.",
                target.WorkflowModule,
                trigger,
                target.Number);
            return;
        }

        if (definitions.Count == 0)
        {
            return;
        }

        _executing = true;

        try
        {
            foreach (var definition in definitions)
            {
                await RunOneAsync(definition, target, trigger, cancellationToken).ConfigureAwait(false);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The record's own change has already been committed by the caller. Losing the run
            // history is bad; failing the user's action after their work was saved is worse,
            // because it reports a failure that did not happen.
            _logger.LogError(
                ex,
                "Workflow execution failed for {Module} {Number} on {Trigger}.",
                target.WorkflowModule,
                target.Number,
                trigger);
        }
        finally
        {
            _executing = false;
        }
    }

    private async Task RunOneAsync(
        WorkflowDefinition definition,
        IWorkflowTarget target,
        WorkflowTrigger trigger,
        CancellationToken cancellationToken)
    {
        var run = NewRun(definition, target, trigger);

        var match = WorkflowConditionEvaluator.Evaluate(definition.Conditions, target.WorkflowFacts);

        if (!match.Matched)
        {
            // Recorded, not discarded. "Why did my rule not fire" is the question people
            // actually have, and it is unanswerable from a log of successes.
            run.Status = WorkflowRunStatus.Skipped;
            run.Outcome = match.Explanation;
            run.CompletedAt = _clock.UtcNow;
            _workflows.AddRun(run);
            return;
        }

        foreach (var action in definition.Actions.OrderBy(a => a.Sequence))
        {
            var step = new WorkflowStepRun
            {
                Sequence = action.Sequence,
                ActionType = action.Type
            };

            try
            {
                var outcome = await ExecuteAsync(action, definition, target, cancellationToken)
                    .ConfigureAwait(false);

                step.Status = outcome.Status;
                step.Detail = outcome.Detail;
                step.Error = outcome.Error;
            }
            catch (Exception ex)
            {
                // One bad action does not stop the rest. A rule pointed at a group somebody
                // deleted should still send the notification that was going to work.
                step.Status = WorkflowStepStatus.Failed;
                step.Error = ex.Message;

                _logger.LogWarning(
                    ex,
                    "Workflow {Workflow} action {Action} failed on {Number}.",
                    definition.Name,
                    action.Type,
                    target.Number);
            }

            run.Steps.Add(step);

            // An action that changed the record raises the trigger that change corresponds to,
            // exactly as a person doing the same thing through the API would. The guard above
            // then refuses it and records why — which is the point: without raising it at all,
            // "automation does not trigger automation" would be an untested claim about a path
            // nothing ever reached, and an administrator whose rule sits on PriorityChanged
            // would have no way to discover that another rule's escalation will not fire it.
            if (step.Status == WorkflowStepStatus.Succeeded && FollowOnTrigger(action.Type) is { } followOn)
            {
                await RunAsync(target, followOn, cancellationToken).ConfigureAwait(false);
            }
        }

        run.Complete(_clock.UtcNow);
        _workflows.AddRun(run);

        definition.LastRunAt = run.CompletedAt;
        definition.RunCount++;

        _audit.Record(
            AuditAction.Update,
            EntityType,
            run.Id.ToString(),
            definition.Name,
            $"Workflow \"{definition.Name}\" ran on {target.Number}: {run.Status}.",
            AuditSource.Workflow);
    }

    /// <summary>
    /// The trigger an action's own effect corresponds to, or none for actions that change
    /// nothing about the record.
    /// </summary>
    private static WorkflowTrigger? FollowOnTrigger(WorkflowActionType type) => type switch
    {
        WorkflowActionType.AssignToGroup or WorkflowActionType.AssignToUser =>
            WorkflowTrigger.AssignmentChanged,

        WorkflowActionType.SetPriority => WorkflowTrigger.PriorityChanged,

        _ => null
    };

    private async Task<StepOutcome> ExecuteAsync(
        WorkflowAction action,
        WorkflowDefinition definition,
        IWorkflowTarget target,
        CancellationToken cancellationToken) => action.Type switch
    {
        WorkflowActionType.NotifyUser or WorkflowActionType.NotifyGroup =>
            await NotifyAsync(action, definition, target, cancellationToken).ConfigureAwait(false),

        WorkflowActionType.AssignToGroup =>
            await AssignToGroupAsync(action, definition, target, cancellationToken).ConfigureAwait(false),

        WorkflowActionType.AssignToUser =>
            await AssignToUserAsync(action, definition, target, cancellationToken).ConfigureAwait(false),

        WorkflowActionType.SetPriority => SetPriority(action, definition, target),

        WorkflowActionType.RequestApproval =>
            await RequestApprovalAsync(action, target, cancellationToken).ConfigureAwait(false),

        _ => new StepOutcome(WorkflowStepStatus.Skipped, $"{action.Type} is not an executable action.")
    };

    private async Task<StepOutcome> NotifyAsync(
        WorkflowAction action,
        WorkflowDefinition definition,
        IWorkflowTarget target,
        CancellationToken cancellationToken)
    {
        var (recipients, description) = await ResolveRecipientsAsync(action, target, cancellationToken)
            .ConfigureAwait(false);

        if (recipients.Count == 0)
        {
            // Not a failure. "Notify the assignee" on an unassigned record has nothing to do,
            // and reporting that as an error would train people to ignore the run history.
            return new StepOutcome(WorkflowStepStatus.Skipped, $"There was no {description} to notify.");
        }

        _notifications.NotifyMany(recipients, new NotificationRequest
        {
            // No actor. The notification comes from a rule, not from whoever happened to touch
            // the record — and the suppression that stops people being told about their own
            // actions would otherwise swallow exactly the notification the rule exists to send.
            ActorUserId = null,
            Kind = NotificationKind.WorkflowNotification,
            Severity = NotificationSeverity.Information,
            Title = $"{target.Number}: {definition.Name}",
            Body = action.RenderMessage(target.Number, target.Title),
            Module = target.WorkflowModule,
            RecordId = target.Id,
            ActionUrl = target.WorkflowActionUrl,
            SendEmail = false
        });

        return new StepOutcome(
            WorkflowStepStatus.Succeeded,
            $"Notified {recipients.Count} {(recipients.Count == 1 ? "person" : "people")} ({description}).");
    }

    private async Task<(IReadOnlyList<Guid> Recipients, string Description)> ResolveRecipientsAsync(
        WorkflowAction action,
        IWorkflowTarget target,
        CancellationToken cancellationToken)
    {
        switch (action.Recipient)
        {
            case WorkflowRecipient.Requester:
                return (Single(target.WorkflowRequesterId), "requester");

            case WorkflowRecipient.Assignee:
                return (Single(target.AssignedToUserId), "assignee");

            case WorkflowRecipient.SpecificUser:
                return (Single(action.TargetUserId), "named recipient");

            case WorkflowRecipient.AssignmentGroup:
                return (await MembersAsync(target.AssignmentGroupId, cancellationToken).ConfigureAwait(false),
                    "assignment group");

            case WorkflowRecipient.SpecificGroup:
                return (await MembersAsync(action.TargetGroupId, cancellationToken).ConfigureAwait(false),
                    "named group");

            default:
                return ([], "recipient");
        }

        static IReadOnlyList<Guid> Single(Guid? id) => id is null ? [] : [id.Value];

        async Task<IReadOnlyList<Guid>> MembersAsync(Guid? groupId, CancellationToken ct)
            => groupId is null
                ? []
                : await _reference.GetGroupMemberIdsAsync(groupId.Value, ct).ConfigureAwait(false);
    }

    private async Task<StepOutcome> AssignToGroupAsync(
        WorkflowAction action,
        WorkflowDefinition definition,
        IWorkflowTarget target,
        CancellationToken cancellationToken)
    {
        if (action.TargetGroupId is null)
        {
            return new StepOutcome(WorkflowStepStatus.Failed, null, "The rule names no group to assign to.");
        }

        // Tenant-filtered, so a rule cannot be pointed at a neighbouring tenant's group: it
        // reads as non-existent rather than routing work across a boundary.
        if (!await _reference.GroupExistsAsync(action.TargetGroupId.Value, cancellationToken).ConfigureAwait(false))
        {
            return new StepOutcome(
                WorkflowStepStatus.Failed,
                null,
                "The group this rule assigns to no longer exists.");
        }

        if (target.AssignmentGroupId == action.TargetGroupId)
        {
            return new StepOutcome(WorkflowStepStatus.Skipped, "The record was already with that group.");
        }

        target.AssignmentGroupId = action.TargetGroupId;

        // Reassigning a group without clearing the assignee would leave the record showing a
        // person who is no longer in the team that owns it.
        target.AssignedToUserId = null;

        RecordRecordChange(definition, target, AuditAction.Assign, "routed to a different group");

        return new StepOutcome(WorkflowStepStatus.Succeeded, "Routed to the group named by the rule.");
    }

    private async Task<StepOutcome> AssignToUserAsync(
        WorkflowAction action,
        WorkflowDefinition definition,
        IWorkflowTarget target,
        CancellationToken cancellationToken)
    {
        if (action.TargetUserId is null)
        {
            return new StepOutcome(WorkflowStepStatus.Failed, null, "The rule names nobody to assign to.");
        }

        if (!await _reference.UserExistsAsync(action.TargetUserId.Value, cancellationToken).ConfigureAwait(false))
        {
            return new StepOutcome(
                WorkflowStepStatus.Failed,
                null,
                "The person this rule assigns to is no longer active.");
        }

        if (target.AssignedToUserId == action.TargetUserId)
        {
            return new StepOutcome(WorkflowStepStatus.Skipped, "That person already held the record.");
        }

        target.AssignedToUserId = action.TargetUserId;

        RecordRecordChange(definition, target, AuditAction.Assign, "assigned to a named person");

        return new StepOutcome(WorkflowStepStatus.Succeeded, "Assigned to the person named by the rule.");
    }

    private StepOutcome SetPriority(
        WorkflowAction action,
        WorkflowDefinition definition,
        IWorkflowTarget target)
    {
        if (action.TargetPriority is null)
        {
            return new StepOutcome(WorkflowStepStatus.Failed, null, "The rule names no priority to set.");
        }

        if ((int)action.TargetPriority.Value >= (int)target.Priority)
        {
            // Not a failure: a rule that raises P3s to P2 meeting a record already at P1 has
            // nothing to do, and the record is already more urgent than the rule wanted.
            return new StepOutcome(
                WorkflowStepStatus.Skipped,
                $"Already at {target.Priority}, which is no lower than {action.TargetPriority}.");
        }

        target.ApplyWorkflowPriority(action.TargetPriority.Value);

        RecordRecordChange(definition, target, AuditAction.Update, $"raised to {action.TargetPriority}");

        return new StepOutcome(WorkflowStepStatus.Succeeded, $"Priority raised to {action.TargetPriority}.");
    }

    private async Task<StepOutcome> RequestApprovalAsync(
        WorkflowAction action,
        IWorkflowTarget target,
        CancellationToken cancellationToken)
    {
        var requirement = (action.TargetUserId, action.TargetGroupId) switch
        {
            (not null, _) => new ApprovalRequirement(
                ApprovalTargetKind.User, action.TargetUserId, null, ApprovalRule.Unanimous),

            (null, not null) => new ApprovalRequirement(
                ApprovalTargetKind.Group, null, action.TargetGroupId, ApprovalRule.AnyOne),

            _ => null
        };

        if (requirement is null)
        {
            return new StepOutcome(WorkflowStepStatus.Failed, null, "The rule names no approver.");
        }

        var raised = await _approvals.RaiseAsync(
                target.WorkflowModule.ToString(),
                target.Id,
                target.Number,
                [requirement],
                cancellationToken)
            .ConfigureAwait(false);

        return raised
            ? new StepOutcome(WorkflowStepStatus.Succeeded, "Approval raised.")
            : new StepOutcome(WorkflowStepStatus.Skipped, "The approver could not be resolved.");
    }

    /// <summary>
    /// Audits a change the engine made to a business record.
    /// <para>
    /// Attributed to the workflow rather than to the person whose action triggered it. An
    /// audit trail saying an agent reassigned a ticket they never touched is worse than no
    /// entry at all, because somebody will be asked about it.
    /// </para>
    /// </summary>
    private void RecordRecordChange(
        WorkflowDefinition definition,
        IWorkflowTarget target,
        AuditAction auditAction,
        string what) =>
        _audit.Record(
            auditAction,
            target.WorkflowModule.ToString(),
            target.Id.ToString(),
            target.Number,
            $"Workflow \"{definition.Name}\" {what}.",
            AuditSource.Workflow);

    private async Task SuppressAsync(
        IWorkflowTarget target,
        WorkflowTrigger trigger,
        CancellationToken cancellationToken)
    {
        try
        {
            var definitions = await _workflows
                .GetRunnableAsync(target.WorkflowModule, trigger, cancellationToken)
                .ConfigureAwait(false);

            foreach (var definition in definitions)
            {
                var run = NewRun(definition, target, trigger);
                run.Status = WorkflowRunStatus.Suppressed;
                run.CompletedAt = _clock.UtcNow;
                run.Outcome =
                    "Not run because another rule's action caused this change. "
                    + "Automation does not trigger automation.";

                _workflows.AddRun(run);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record suppressed workflow runs for {Number}.", target.Number);
        }
    }

    private WorkflowRun NewRun(
        WorkflowDefinition definition,
        IWorkflowTarget target,
        WorkflowTrigger trigger) => new()
    {
        TenantId = target.TenantId,
        WorkflowDefinitionId = definition.Id,
        WorkflowName = definition.Name,
        Module = target.WorkflowModule,
        RecordId = target.Id,
        RecordNumber = target.Number,
        Trigger = trigger,
        StartedAt = _clock.UtcNow
    };

    /// <param name="Status">What happened.</param>
    /// <param name="Detail">What was done, for the run history.</param>
    /// <param name="Error">Why it did not happen.</param>
    private readonly record struct StepOutcome(WorkflowStepStatus Status, string? Detail, string? Error = null);
}
