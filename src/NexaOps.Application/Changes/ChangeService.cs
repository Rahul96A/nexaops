using FluentValidation;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Incidents;
using NexaOps.Application.Requests;
using NexaOps.Application.Security;
using NexaOps.Domain.Approvals;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Changes;
using NexaOps.Domain.Common;

namespace NexaOps.Application.Changes;

/// <summary>
/// The only way a change moves.
/// <para>
/// The module's job is to make the trade-off between speed and safety explicit, so the type of
/// change decides how it is authorised and the service records which route was taken.
/// </para>
/// </summary>
public interface IChangeService
{
    Task<PagedResult<ChangeSummaryDto>> SearchAsync(ChangeQuery query, CancellationToken ct = default);

    Task<ChangeDetailDto> GetAsync(Guid id, CancellationToken ct = default);

    Task<ChangeDetailDto> GetByNumberAsync(string number, CancellationToken ct = default);

    Task<ChangeSummaryCountsDto> GetSummaryAsync(CancellationToken ct = default);

    Task<ChangeDetailDto> CreateAsync(CreateChangeCommand command, CancellationToken ct = default);

    Task<ChangeDetailDto> UpdateAsync(Guid id, UpdateChangeCommand command, CancellationToken ct = default);

    Task<ChangeDetailDto> ScheduleAsync(Guid id, ScheduleChangeCommand command, CancellationToken ct = default);

    Task<ChangeDetailDto> AssignAsync(Guid id, AssignChangeCommand command, CancellationToken ct = default);

    Task<ChangeDetailDto> ChangeStatusAsync(Guid id, ChangeStatusCommand command, CancellationToken ct = default);

    Task<ChangeDetailDto> SubmitForApprovalAsync(Guid id, CancellationToken ct = default);

    Task<ChangeDetailDto> ReviewAsync(Guid id, ReviewChangeCommand command, CancellationToken ct = default);

    Task<ChangeDetailDto> CancelAsync(Guid id, CancelChangeCommand command, CancellationToken ct = default);

    Task<IReadOnlyList<ChangeCommentDto>> GetCommentsAsync(Guid id, CancellationToken ct = default);

    Task<ChangeCommentDto> AddCommentAsync(Guid id, AddChangeCommentCommand command, CancellationToken ct = default);

    /// <summary>Applies an approval decision made on a change, from the shared approval flow.</summary>
    Task<ChangeDetailDto> ApplyApprovalOutcomeAsync(
        Guid changeId,
        ApprovalRecordOutcome outcome,
        string? comment,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class ChangeService : IChangeService
{
    private const string EntityType = nameof(Change);
    private const string ApprovalModule = "Change";
    private const string NumberSequenceKey = "CHG";

    private static readonly string[] SortableFields =
        ["createdAt", "number", "plannedStartAt", "risk", "status", "priority"];

    private readonly IChangeRepository _changes;
    private readonly IChangeQueryService _queries;
    private readonly IApprovalService _approvals;
    private readonly IServiceDeskReferenceRepository _reference;
    private readonly INumberSequenceService _numbers;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ChangeService> _logger;

    public ChangeService(
        IChangeRepository changes,
        IChangeQueryService queries,
        IApprovalService approvals,
        IServiceDeskReferenceRepository reference,
        INumberSequenceService numbers,
        IAuditService audit,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<ChangeService> logger)
    {
        _changes = changes;
        _queries = queries;
        _approvals = approvals;
        _reference = reference;
        _numbers = numbers;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    // -----------------------------------------------------------------
    // Reads
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public Task<PagedResult<ChangeSummaryDto>> SearchAsync(ChangeQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        _currentUser.DemandPermission(Permissions.ChangeRead);

        if (!SortableFields.Contains(query.SortBy, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(query.SortBy),
                    $"Sort field must be one of: {string.Join(", ", SortableFields)}.")
            ]);
        }

        return _queries.SearchAsync(query, ct);
    }

    /// <inheritdoc />
    public async Task<ChangeDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.ChangeRead);

        return await _queries.GetAsync(id, ct).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(EntityType, id);
    }

    /// <inheritdoc />
    public async Task<ChangeDetailDto> GetByNumberAsync(string number, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.ChangeRead);

        return await _queries.GetByNumberAsync(number, ct).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(EntityType, number);
    }

    /// <inheritdoc />
    public Task<ChangeSummaryCountsDto> GetSummaryAsync(CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.ChangeRead);
        return _queries.GetSummaryAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChangeCommentDto>> GetCommentsAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.ChangeRead);

        return await _queries.GetCommentsAsync(id, ct).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(EntityType, id);
    }

    // -----------------------------------------------------------------
    // Writes
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<ChangeDetailDto> CreateAsync(CreateChangeCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.ChangeCreate);

        // The narrow permission that stops the emergency path becoming the normal one.
        if (command.Type == ChangeType.Emergency)
        {
            _currentUser.DemandPermission(Permissions.ChangeRaiseEmergency);
        }

        if (string.IsNullOrWhiteSpace(command.Title))
        {
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(command.Title), "A short summary is required.")
            ]);
        }

        if (command.CategoryId is not null)
        {
            _ = await _reference.GetCategoryAsync(command.CategoryId.Value, ct).ConfigureAwait(false)
                ?? throw new EntityNotFoundException("Category", command.CategoryId.Value);
        }

        var number = await _numbers.NextAsync(NumberSequenceKey, ct).ConfigureAwait(false);

        var change = new Change
        {
            Number = number,
            Title = command.Title.Trim(),
            Description = command.Description?.Trim() ?? string.Empty,
            Type = command.Type,
            Risk = command.Risk,
            Impact = command.Impact,
            Priority = command.Priority,
            CategoryId = command.CategoryId,
            ImplementationPlan = command.ImplementationPlan?.Trim(),
            RollbackPlan = command.RollbackPlan?.Trim(),
            TestPlan = command.TestPlan?.Trim(),
            ImpactAssessment = command.ImpactAssessment?.Trim(),
            RequiresDowntime = command.RequiresDowntime,
            ProblemId = command.ProblemId,
            RequestedByUserId = _currentUser.UserId,
            Status = ChangeStatus.Draft
        };

        if (command.PlannedStartAt is not null && command.PlannedEndAt is not null)
        {
            change.Schedule(command.PlannedStartAt.Value, command.PlannedEndAt.Value);
        }

        _changes.Add(change);

        _audit.Record(
            AuditAction.Create,
            EntityType,
            change.Id.ToString(),
            change.Number,
            $"Raised {change.Number} as a {change.Type} change at {change.Risk} risk.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Change {Number} raised by {UserId}: {Type}, {Risk} risk.",
            change.Number, _currentUser.UserId, change.Type, change.Risk);

        return await ReloadAsync(change.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ChangeDetailDto> UpdateAsync(Guid id, UpdateChangeCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.ChangeUpdate);

        var change = await LoadAsync(id, ct).ConfigureAwait(false);
        ApplyConcurrencyToken(change, command.RowVersion);

        if (!string.IsNullOrWhiteSpace(command.Title))
        {
            change.Title = command.Title.Trim();
        }

        if (command.Description is not null)
        {
            change.Description = command.Description.Trim();
        }

        if (command.Risk is not null)
        {
            change.Risk = command.Risk.Value;
        }

        if (command.Impact is not null)
        {
            change.Impact = command.Impact.Value;
        }

        if (command.Priority is not null)
        {
            change.Priority = command.Priority.Value;
        }

        if (command.CategoryId is not null)
        {
            _ = await _reference.GetCategoryAsync(command.CategoryId.Value, ct).ConfigureAwait(false)
                ?? throw new EntityNotFoundException("Category", command.CategoryId.Value);

            change.CategoryId = command.CategoryId;
        }

        if (command.ImplementationPlan is not null)
        {
            change.ImplementationPlan = command.ImplementationPlan.Trim();
        }

        if (command.RollbackPlan is not null)
        {
            change.RollbackPlan = command.RollbackPlan.Trim();
        }

        if (command.TestPlan is not null)
        {
            change.TestPlan = command.TestPlan.Trim();
        }

        if (command.ImpactAssessment is not null)
        {
            change.ImpactAssessment = command.ImpactAssessment.Trim();
        }

        if (command.RequiresDowntime is not null)
        {
            change.RequiresDowntime = command.RequiresDowntime.Value;
        }

        _audit.Record(AuditAction.Update, EntityType, change.Id.ToString(), change.Number, "Change updated.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(change.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ChangeDetailDto> ScheduleAsync(Guid id, ScheduleChangeCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.ChangeSchedule);

        var change = await LoadAsync(id, ct).ConfigureAwait(false);
        ApplyConcurrencyToken(change, command.RowVersion);

        change.Schedule(command.PlannedStartAt, command.PlannedEndAt);

        _audit.Record(
            AuditAction.Update,
            EntityType,
            change.Id.ToString(),
            change.Number,
            $"Window set to {command.PlannedStartAt:u} - {command.PlannedEndAt:u}.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(change.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ChangeDetailDto> AssignAsync(Guid id, AssignChangeCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.ChangeAssign);

        var change = await LoadAsync(id, ct).ConfigureAwait(false);
        ApplyConcurrencyToken(change, command.RowVersion);

        if (command.AssignmentGroupId is not null
            && !await _reference.GroupExistsAsync(command.AssignmentGroupId.Value, ct).ConfigureAwait(false))
        {
            throw new EntityNotFoundException("Group", command.AssignmentGroupId.Value);
        }

        if (command.AssignedToUserId is not null
            && !await _reference.UserExistsAsync(command.AssignedToUserId.Value, ct).ConfigureAwait(false))
        {
            throw new EntityNotFoundException("User", command.AssignedToUserId.Value);
        }

        change.Assign(command.AssignmentGroupId, command.AssignedToUserId);

        _audit.Record(AuditAction.Assign, EntityType, change.Id.ToString(), change.Number, "Assignment changed.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(change.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ChangeDetailDto> SubmitForApprovalAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.ChangeUpdate);

        var change = await LoadAsync(id, ct).ConfigureAwait(false);
        var now = _clock.UtcNow;

        if (!ChangeStateMachine.RequiresApprovalBeforeScheduling(change.Type))
        {
            // A standard change was approved once, when its procedure was accepted; an emergency
            // change is reviewed afterwards. Neither routes to the board now.
            throw new DomainException(
                "change.approval_not_required",
                $"A {change.Type} change does not require approval before it is scheduled.");
        }

        change.TransitionTo(ChangeStatus.AwaitingApproval, _currentUser.UserId, now);

        // Reuses the module-agnostic approval infrastructure built for requests: the change
        // advisory board is an approving group, and the stage arithmetic is unchanged.
        var boardId = await _reference.GetChangeAdvisoryBoardIdAsync(ct).ConfigureAwait(false);

        var raised = boardId is not null
            && await _approvals.RaiseAsync(
                    ApprovalModule,
                    change.Id,
                    $"{change.Number} — {change.Title}",
                    [new ApprovalRequirement(ApprovalTargetKind.Group, null, boardId, ApprovalRule.AnyOne)],
                    ct)
                .ConfigureAwait(false);

        if (!raised)
        {
            // No board is configured, so there is nobody to ask. Stranding the change would be
            // worse than proceeding, and the audit trail records that no approval was sought.
            _logger.LogWarning(
                "Change {Number} required approval but no change advisory board is configured; scheduling directly.",
                change.Number);

            change.TransitionTo(ChangeStatus.Scheduled, _currentUser.UserId, now);

            _audit.Record(
                AuditAction.Update,
                EntityType,
                change.Id.ToString(),
                change.Number,
                "Approval required but no change advisory board is configured; scheduled without approval.",
                outcome: AuditOutcome.Failure);
        }
        else
        {
            _audit.Record(
                AuditAction.Update,
                EntityType,
                change.Id.ToString(),
                change.Number,
                "Submitted to the change advisory board.");
        }

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(change.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ChangeDetailDto> ChangeStatusAsync(Guid id, ChangeStatusCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        _currentUser.DemandPermission(command.Status switch
        {
            ChangeStatus.Scheduled => Permissions.ChangeSchedule,
            ChangeStatus.Implementing => Permissions.ChangeImplement,
            ChangeStatus.Review => Permissions.ChangeImplement,
            ChangeStatus.Closed => Permissions.ChangeClose,
            ChangeStatus.Cancelled => Permissions.ChangeCancel,
            _ => Permissions.ChangeUpdate
        });

        var change = await LoadAsync(id, ct).ConfigureAwait(false);
        ApplyConcurrencyToken(change, command.RowVersion);

        var previous = change.Status;
        change.TransitionTo(command.Status, _currentUser.UserId, _clock.UtcNow);

        _audit.Record(
            AuditAction.Update,
            EntityType,
            change.Id.ToString(),
            change.Number,
            $"Status changed from {previous} to {change.Status}." +
            (string.IsNullOrWhiteSpace(command.Note) ? string.Empty : $" {command.Note.Trim()}"));

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(change.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ChangeDetailDto> ReviewAsync(Guid id, ReviewChangeCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.ChangeReview);

        var change = await LoadAsync(id, ct).ConfigureAwait(false);
        ApplyConcurrencyToken(change, command.RowVersion);

        change.RecordReview(command.Outcome, command.Notes, _currentUser.UserId, _clock.UtcNow);

        _audit.Record(
            AuditAction.Update,
            EntityType,
            change.Id.ToString(),
            change.Number,
            $"Post-implementation review recorded: {command.Outcome}.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(change.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ChangeDetailDto> CancelAsync(Guid id, CancelChangeCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.ChangeCancel);

        var change = await LoadAsync(id, ct).ConfigureAwait(false);
        ApplyConcurrencyToken(change, command.RowVersion);

        change.Cancel(command.Reason, _currentUser.UserId, _clock.UtcNow);

        await _approvals.CancelOutstandingAsync(ApprovalModule, change.Id, ct).ConfigureAwait(false);

        _audit.Record(
            AuditAction.Update,
            EntityType,
            change.Id.ToString(),
            change.Number,
            $"Change cancelled. {change.CancellationReason}");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(change.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ChangeCommentDto> AddCommentAsync(
        Guid id,
        AddChangeCommentCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.ChangeCommentCreate);

        if (string.IsNullOrWhiteSpace(command.Body))
        {
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(nameof(command.Body), "A comment cannot be empty.")
            ]);
        }

        var change = await LoadAsync(id, ct).ConfigureAwait(false);

        var comment = new ChangeComment
        {
            ChangeId = change.Id,
            Kind = command.Kind,
            Body = command.Body.Trim(),
            AuthorId = _currentUser.UserId,
            AuthorDisplayName = _currentUser.DisplayName
        };

        _changes.AddComment(comment);

        _audit.Record(AuditAction.Update, EntityType, change.Id.ToString(), change.Number, "Comment added.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return new ChangeCommentDto(
            comment.Id, comment.Kind, comment.Body, comment.AuthorId,
            comment.AuthorDisplayName, comment.CreatedAt);
    }

    /// <inheritdoc />
    public async Task<ChangeDetailDto> ApplyApprovalOutcomeAsync(
        Guid changeId,
        ApprovalRecordOutcome outcome,
        string? comment,
        CancellationToken ct = default)
    {
        var change = await LoadAsync(changeId, ct).ConfigureAwait(false);
        var now = _clock.UtcNow;

        switch (outcome)
        {
            case ApprovalRecordOutcome.Approved:
                change.TransitionTo(ChangeStatus.Scheduled, _currentUser.UserId, now);
                break;

            case ApprovalRecordOutcome.Rejected:
                change.RecordRejected(comment ?? "No reason given.", _currentUser.UserId, now);
                await _approvals.CancelOutstandingAsync(ApprovalModule, change.Id, ct).ConfigureAwait(false);
                break;
        }

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(change.Id, ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private async Task<Change> LoadAsync(Guid id, CancellationToken ct)
        => await _changes.GetAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(EntityType, id);

    private async Task<ChangeDetailDto> ReloadAsync(Guid id, CancellationToken ct)
        => await _queries.GetAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(EntityType, id);

    private void ApplyConcurrencyToken(Change change, byte[]? rowVersion)
    {
        if (rowVersion is { Length: > 0 })
        {
            _changes.SetExpectedVersion(change, rowVersion);
        }
    }
}
