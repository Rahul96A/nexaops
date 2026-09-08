using NexaOps.Application.Common;
using NexaOps.Application.Requests;
using NexaOps.Domain.Changes;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Application.Changes;

public sealed record ChangeSummaryDto(
    Guid Id,
    string Number,
    string Title,
    ChangeType Type,
    ChangeStatus Status,
    ChangeRisk Risk,
    Priority Priority,
    ChangeOutcome? Outcome,
    Guid? AssignedToUserId,
    string? AssignedToName,
    string? AssignedToAvatarColor,
    Guid? AssignmentGroupId,
    string? AssignmentGroupName,
    DateTimeOffset? PlannedStartAt,
    DateTimeOffset? PlannedEndAt,
    bool RequiresDowntime,
    DateTimeOffset CreatedAt);

public sealed record ChangeCommentDto(
    Guid Id,
    IncidentCommentKind Kind,
    string Body,
    Guid AuthorId,
    string AuthorDisplayName,
    DateTimeOffset CreatedAt);

/// <param name="CollidingChanges">
/// Other scheduled changes whose window overlaps this one. A warning, not a block: two changes
/// in one window is sometimes exactly the intent.
/// </param>
public sealed record ChangeDetailDto(
    Guid Id,
    string Number,
    string Title,
    string Description,
    ChangeType Type,
    ChangeStatus Status,
    ChangeRisk Risk,
    Impact Impact,
    Priority Priority,
    string? ImplementationPlan,
    string? RollbackPlan,
    string? TestPlan,
    string? ImpactAssessment,
    DateTimeOffset? PlannedStartAt,
    DateTimeOffset? PlannedEndAt,
    DateTimeOffset? ActualStartAt,
    DateTimeOffset? ActualEndAt,
    bool RequiresDowntime,
    Guid? AssignmentGroupId,
    string? AssignmentGroupName,
    Guid? AssignedToUserId,
    string? AssignedToName,
    Guid RequestedByUserId,
    string RequestedByName,
    Guid? CategoryId,
    string? CategoryName,
    Guid? ProblemId,
    string? ProblemNumber,
    ChangeOutcome? Outcome,
    string? ReviewNotes,
    string? RejectionReason,
    string? CancellationReason,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ApprovalDto> Approvals,
    IReadOnlyList<ChangeSummaryDto> CollidingChanges,
    IReadOnlyList<ChangeStatus> AllowedTransitions,
    byte[]? RowVersion);

public sealed record ChangeSummaryCountsDto(
    int OpenChanges,
    int AwaitingApproval,
    int ScheduledThisWeek,
    int Implementing,
    int AwaitingReview,
    int AssignedToMe,
    int EmergencyThisMonth,
    IReadOnlyList<ChangeStatusCountDto> OpenByStatus);

public sealed record ChangeStatusCountDto(ChangeStatus Status, int Count);

// ---------------------------------------------------------------------------
// Commands
// ---------------------------------------------------------------------------

public sealed class CreateChangeCommand
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ChangeType Type { get; set; } = ChangeType.Normal;
    public ChangeRisk Risk { get; set; } = ChangeRisk.Medium;
    public Impact Impact { get; set; } = Impact.Moderate;
    public Priority Priority { get; set; } = Priority.P3Moderate;
    public Guid? CategoryId { get; set; }
    public string? ImplementationPlan { get; set; }
    public string? RollbackPlan { get; set; }
    public string? TestPlan { get; set; }
    public string? ImpactAssessment { get; set; }
    public DateTimeOffset? PlannedStartAt { get; set; }
    public DateTimeOffset? PlannedEndAt { get; set; }
    public bool RequiresDowntime { get; set; }

    /// <summary>The problem whose permanent fix this change delivers.</summary>
    public Guid? ProblemId { get; set; }
}

public sealed class UpdateChangeCommand
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public ChangeRisk? Risk { get; set; }
    public Impact? Impact { get; set; }
    public Priority? Priority { get; set; }
    public Guid? CategoryId { get; set; }
    public string? ImplementationPlan { get; set; }
    public string? RollbackPlan { get; set; }
    public string? TestPlan { get; set; }
    public string? ImpactAssessment { get; set; }
    public bool? RequiresDowntime { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class ScheduleChangeCommand
{
    public DateTimeOffset PlannedStartAt { get; set; }
    public DateTimeOffset PlannedEndAt { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class AssignChangeCommand
{
    public Guid? AssignmentGroupId { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class ChangeStatusCommand
{
    public ChangeStatus Status { get; set; }
    public string? Note { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class ReviewChangeCommand
{
    public ChangeOutcome Outcome { get; set; }
    public string Notes { get; set; } = string.Empty;
    public byte[]? RowVersion { get; set; }
}

public sealed class CancelChangeCommand
{
    public string? Reason { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class AddChangeCommentCommand
{
    public string Body { get; set; } = string.Empty;
    public IncidentCommentKind Kind { get; set; } = IncidentCommentKind.WorkNote;
}

public sealed class ChangeQuery
{
    public string? Search { get; set; }
    public List<ChangeStatus>? Statuses { get; set; }
    public List<ChangeType>? Types { get; set; }
    public List<ChangeRisk>? Risks { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public bool? OpenOnly { get; set; }

    /// <summary>Window filter for the change calendar.</summary>
    public DateTimeOffset? WindowFrom { get; set; }
    public DateTimeOffset? WindowTo { get; set; }

    /// <summary>Named scope, e.g. <c>my-work</c>, <c>awaiting-approval</c>, <c>this-week</c>.</summary>
    public string? Scope { get; set; }

    public string SortBy { get; set; } = "plannedStartAt";
    public bool SortDescending { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

/// <summary>Write-side access to change aggregates.</summary>
public interface IChangeRepository
{
    Task<Change?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Change?> GetByNumberAsync(string number, CancellationToken cancellationToken = default);

    void Add(Change change);

    void AddComment(ChangeComment comment);

    void SetExpectedVersion(Change change, byte[] rowVersion);
}

/// <summary>Read-side queries for changes.</summary>
public interface IChangeQueryService
{
    Task<PagedResult<ChangeSummaryDto>> SearchAsync(
        ChangeQuery query,
        CancellationToken cancellationToken = default);

    Task<ChangeDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ChangeDetailDto?> GetByNumberAsync(string number, CancellationToken cancellationToken = default);

    /// <summary>Null when the caller cannot see the change, which the service turns into a 404.</summary>
    Task<IReadOnlyList<ChangeCommentDto>?> GetCommentsAsync(
        Guid changeId,
        CancellationToken cancellationToken = default);

    Task<ChangeSummaryCountsDto> GetSummaryAsync(CancellationToken cancellationToken = default);
}
