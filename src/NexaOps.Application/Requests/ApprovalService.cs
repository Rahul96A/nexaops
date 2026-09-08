using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Incidents;
using NexaOps.Application.Notifications;
using NexaOps.Application.Security;
using NexaOps.Domain.Approvals;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Catalog;
using NexaOps.Domain.Common;
using NexaOps.Domain.Platform;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Application.Requests;

/// <summary>What a stage decision means for the record that owns it.</summary>
public enum ApprovalRecordOutcome
{
    StillPending = 1,
    Approved = 2,
    Rejected = 3
}

/// <summary>
/// Raises approvals, records decisions, and reports what a decision means for the owning record.
/// <para>
/// Deliberately knows nothing about service requests specifically. It works in terms of
/// <c>module</c> and <c>recordId</c>, so change management reuses it unchanged.
/// </para>
/// </summary>
public interface IApprovalService
{
    /// <summary>
    /// Creates the approvals a record needs, one stage per distinct requirement.
    /// </summary>
    /// <returns>True when at least one live approval was raised.</returns>
    Task<bool> RaiseAsync(
        string module,
        Guid recordId,
        string recordLabel,
        IReadOnlyList<ApprovalRequirement> requirements,
        CancellationToken cancellationToken = default);

    /// <summary>Records one approver's decision and evaluates what it means for the record.</summary>
    Task<ApprovalDecisionResult> DecideAsync(
        Guid approvalId,
        DecideApprovalCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Withdraws every outstanding approval on a record that has been cancelled.</summary>
    Task CancelOutstandingAsync(
        string module,
        Guid recordId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The outcome of one decision, together with the record it belongs to, so the calling module
/// does not have to look the approval up a second time to find out what it just decided.
/// </summary>
public sealed record ApprovalDecisionResult(
    ApprovalRecordOutcome Outcome,
    string Module,
    Guid RecordId);

/// <param name="TargetKind">Who must decide.</param>
/// <param name="ApproverUserId">Named approver, or the resolved manager.</param>
/// <param name="ApproverGroupId">Approving group.</param>
/// <param name="Rule">How approvers within the stage combine.</param>
public sealed record ApprovalRequirement(
    ApprovalTargetKind TargetKind,
    Guid? ApproverUserId,
    Guid? ApproverGroupId,
    ApprovalRule Rule);

/// <inheritdoc />
public sealed class ApprovalService : IApprovalService
{
    private const string EntityType = nameof(Approval);

    private readonly IApprovalRepository _approvals;
    private readonly IServiceDeskReferenceRepository _reference;
    private readonly INotificationService _notifications;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ApprovalService> _logger;

    public ApprovalService(
        IApprovalRepository approvals,
        IServiceDeskReferenceRepository reference,
        INotificationService notifications,
        IAuditService audit,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<ApprovalService> logger)
    {
        _approvals = approvals;
        _reference = reference;
        _notifications = notifications;
        _audit = audit;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> RaiseAsync(
        string module,
        Guid recordId,
        string recordLabel,
        IReadOnlyList<ApprovalRequirement> requirements,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        var raised = 0;
        var stage = 1;

        foreach (var requirement in requirements)
        {
            // A requirement that resolves to nobody is skipped rather than stored as an
            // unanswerable approval. A requester with no line manager would otherwise leave the
            // record waiting on a person who does not exist.
            if (requirement.ApproverUserId is null && requirement.ApproverGroupId is null)
            {
                _logger.LogInformation(
                    "Approval requirement of kind {Kind} on {Module} {RecordId} resolved to nobody and was skipped.",
                    requirement.TargetKind, module, recordId);
                continue;
            }

            var approval = new Approval
            {
                Module = module,
                RecordId = recordId,
                RecordLabel = recordLabel,
                Stage = stage++,
                Rule = requirement.Rule,
                TargetKind = requirement.TargetKind,
                ApproverUserId = requirement.ApproverUserId,
                ApproverGroupId = requirement.ApproverGroupId,
                State = ApprovalState.Pending
            };

            _approvals.Add(approval);
            raised++;

            NotifyApprover(approval, recordLabel);
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return raised > 0;
    }

    /// <inheritdoc />
    public async Task<ApprovalDecisionResult> DecideAsync(
        Guid approvalId,
        DecideApprovalCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        _currentUser.DemandPermission(Permissions.ApprovalAct);

        var approval = await _approvals.GetAsync(approvalId, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(EntityType, approvalId);

        // Holding approval.act is not enough. The record itself decides who may decide it, which
        // is what stops one approver clearing somebody else's authorisation.
        await EnsureCallerMayDecideAsync(approval, cancellationToken).ConfigureAwait(false);

        var now = _clock.UtcNow;

        if (command.Approved)
        {
            approval.Approve(_currentUser.UserId, now, command.Comment);
        }
        else
        {
            approval.Reject(_currentUser.UserId, now, command.Comment ?? string.Empty);
        }

        // Peers in the same AnyOne stage no longer need to act. They are marked NotRequired
        // rather than Cancelled, because "somebody else got there first" and "the whole request
        // went away" are different facts.
        var stageApprovals = await _approvals
            .GetForRecordAsync(approval.Module, approval.RecordId, cancellationToken)
            .ConfigureAwait(false);

        var sameStage = stageApprovals.Where(a => a.Stage == approval.Stage).ToList();

        if (approval.Rule == ApprovalRule.AnyOne)
        {
            foreach (var peer in sameStage.Where(a => a.Id != approval.Id))
            {
                peer.MarkNotRequired(now);
            }
        }

        _audit.Record(
            AuditAction.Update,
            EntityType,
            approval.Id.ToString(),
            approval.RecordLabel,
            command.Approved
                ? $"Approved stage {approval.Stage}."
                : $"Rejected stage {approval.Stage}: {approval.Comment}");

        // The requester is notified by the owning module's service, which knows who they are.
        return new ApprovalDecisionResult(
            EvaluateRecord(stageApprovals), approval.Module, approval.RecordId);
    }

    /// <inheritdoc />
    public async Task CancelOutstandingAsync(
        string module,
        Guid recordId,
        CancellationToken cancellationToken = default)
    {
        var approvals = await _approvals
            .GetForRecordAsync(module, recordId, cancellationToken)
            .ConfigureAwait(false);

        var now = _clock.UtcNow;

        foreach (var approval in approvals)
        {
            approval.CancelIfOutstanding(now);
        }
    }

    /// <summary>
    /// Walks the stages in order. The record is approved only once every stage has cleared, and
    /// rejected the moment any stage is refused.
    /// </summary>
    private static ApprovalRecordOutcome EvaluateRecord(IReadOnlyList<Approval> approvals)
    {
        if (approvals.Count == 0)
        {
            return ApprovalRecordOutcome.Approved;
        }

        foreach (var stage in approvals.GroupBy(a => a.Stage).OrderBy(g => g.Key))
        {
            var outcome = ApprovalStateMachine.Evaluate(
                stage.Select(a => a.State).ToList(),
                stage.First().Rule);

            switch (outcome)
            {
                case ApprovalStateMachine.StageOutcome.Rejected:
                    return ApprovalRecordOutcome.Rejected;

                case ApprovalStateMachine.StageOutcome.Pending:
                    return ApprovalRecordOutcome.StillPending;
            }
        }

        return ApprovalRecordOutcome.Approved;
    }

    /// <summary>
    /// Confirms the caller is actually the person this approval is addressed to.
    /// </summary>
    private async Task EnsureCallerMayDecideAsync(Approval approval, CancellationToken cancellationToken)
    {
        if (approval.ApproverUserId is not null && approval.ApproverUserId == _currentUser.UserId)
        {
            return;
        }

        if (approval.ApproverGroupId is not null
            && await _reference
                .IsMemberOfGroupAsync(_currentUser.UserId, approval.ApproverGroupId.Value, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        // Reported as not found rather than forbidden, for the same reason a cross-tenant record
        // is: confirming the approval exists tells the caller about work they are not part of.
        throw new EntityNotFoundException(EntityType, approval.Id);
    }

    private void NotifyApprover(Approval approval, string recordLabel)
    {
        if (approval.ApproverUserId is null)
        {
            // Group approvals notify on the group's own channel, which is not built yet. The
            // approval still appears in every member's queue, so nothing is lost - only the
            // push. Stated rather than silently skipped.
            return;
        }

        _notifications.Notify(new NotificationRequest
        {
            RecipientUserId = approval.ApproverUserId.Value,
            ActorUserId = _currentUser.UserIdOrNull,
            Kind = NotificationKind.ApprovalRequested,
            Severity = NotificationSeverity.Warning,
            Title = "Approval needed",
            Body = $"{recordLabel} is waiting for your approval.",
            Module = ServiceModule.Request,
            RecordId = approval.RecordId,
            ActionUrl = $"/approvals",
            SendEmail = true
        });
    }
}
