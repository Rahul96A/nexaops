using NexaOps.Domain.Approvals;
using NexaOps.Domain.Catalog;
using NexaOps.Domain.Requests;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Application.Requests;

// ---------------------------------------------------------------------------
// Catalogue
// ---------------------------------------------------------------------------

/// <param name="Key">Stable key the answer is submitted under.</param>
/// <param name="Choices">Allowed values for Choice and MultiChoice, empty otherwise.</param>
public sealed record CatalogVariableDto(
    Guid Id,
    string Key,
    string Label,
    string? HelpText,
    VariableType Type,
    bool IsRequired,
    int SortOrder,
    string? DefaultValue,
    IReadOnlyList<string> Choices,
    int? MaxLength,
    decimal? MinValue,
    decimal? MaxValue);

/// <summary>A catalogue tile. Deliberately does not carry the variable list, which is only
/// needed once somebody opens the item.</summary>
public sealed record CatalogItemSummaryDto(
    Guid Id,
    string Code,
    string Name,
    string ShortDescription,
    Guid? CategoryId,
    string? CategoryName,
    CatalogItemStatus Status,
    decimal? Cost,
    int? EstimatedDeliveryDays,
    string? Icon,
    bool RequiresApproval,
    int SortOrder);

public sealed record CatalogItemDetailDto(
    Guid Id,
    string Code,
    string Name,
    string ShortDescription,
    string Description,
    Guid? CategoryId,
    string? CategoryName,
    CatalogItemStatus Status,
    Priority Priority,
    decimal? Cost,
    int? EstimatedDeliveryDays,
    string? Icon,
    int? MaxQuantity,
    bool RequiresApproval,
    ApprovalTargetKind ApprovalTargetKind,
    string? ApproverName,
    Guid? FulfilmentGroupId,
    string? FulfilmentGroupName,
    IReadOnlyList<CatalogVariableDto> Variables,
    byte[]? RowVersion);

// ---------------------------------------------------------------------------
// Requests
// ---------------------------------------------------------------------------

public sealed record RequestItemDto(
    Guid Id,
    Guid CatalogItemId,
    string CatalogItemName,
    int Quantity,
    decimal? UnitCost,
    decimal? LineCost,
    RequestItemStatus Status,
    IReadOnlyDictionary<string, string> Values,
    Guid? FulfilmentGroupId,
    string? FulfilmentGroupName,
    Guid? AssignedToUserId,
    string? AssignedToName,
    DateTimeOffset? FulfilledAt,
    string? FulfilmentNotes);

public sealed record ApprovalDto(
    Guid Id,
    int Stage,
    ApprovalRule Rule,
    ApprovalTargetKind TargetKind,
    Guid? ApproverUserId,
    string? ApproverName,
    Guid? ApproverGroupId,
    string? ApproverGroupName,
    ApprovalState State,
    Guid? DecidedByUserId,
    string? DecidedByName,
    DateTimeOffset? DecidedAt,
    string? Comment,
    string RecordLabel,
    string Module,
    Guid RecordId,
    string? RecordNumber);

public sealed record RequestCommentDto(
    Guid Id,
    IncidentCommentKind Kind,
    string Body,
    Guid AuthorId,
    string AuthorDisplayName,
    DateTimeOffset CreatedAt);

/// <summary>A row in the request queue.</summary>
public sealed record RequestSummaryDto(
    Guid Id,
    string Number,
    string Title,
    RequestStatus Status,
    Priority Priority,
    Guid RequesterId,
    string RequesterName,
    Guid RequestedForId,
    string RequestedForName,
    Guid? AssignedToUserId,
    string? AssignedToName,
    string? AssignedToAvatarColor,
    Guid? FulfilmentGroupId,
    string? FulfilmentGroupName,
    int ItemCount,
    decimal? TotalCost,
    bool HasBreachedSla,
    DateTimeOffset? NextSlaDueAt,
    DateTimeOffset CreatedAt);

public sealed record RequestDetailDto(
    Guid Id,
    string Number,
    string Title,
    string Description,
    RequestStatus Status,
    RequestPendingReason? PendingReason,
    RequestChannel Channel,
    Priority Priority,
    Guid RequesterId,
    string RequesterName,
    Guid RequestedForId,
    string RequestedForName,
    Guid? CategoryId,
    string? CategoryName,
    Guid? FulfilmentGroupId,
    string? FulfilmentGroupName,
    Guid? AssignedToUserId,
    string? AssignedToName,
    DateOnly? RequiredByDate,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? FulfilledAt,
    DateTimeOffset? ClosedAt,
    string? RejectionReason,
    string? CancellationReason,
    decimal? TotalCost,
    bool HasBreachedSla,
    DateTimeOffset? NextSlaDueAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RequestItemDto> Items,
    IReadOnlyList<ApprovalDto> Approvals,
    IReadOnlyList<RequestStatus> AllowedTransitions,
    byte[]? RowVersion);

/// <summary>The counters on the request side of the service desk dashboard.</summary>
public sealed record RequestSummaryCountsDto(
    int OpenRequests,
    int AwaitingApproval,
    int AwaitingMyApproval,
    int Unassigned,
    int AssignedToMe,
    int RaisedByMe,
    int BreachedOpen,
    int FulfilledToday,
    int CreatedToday,
    IReadOnlyList<RequestStatusCountDto> OpenByStatus);

public sealed record RequestStatusCountDto(RequestStatus Status, int Count);

// ---------------------------------------------------------------------------
// Commands
// ---------------------------------------------------------------------------

/// <param name="Values">Answers keyed by <see cref="CatalogVariableDto.Key"/>.</param>
public sealed class RequestLineInput
{
    public Guid CatalogItemId { get; set; }
    public int Quantity { get; set; } = 1;
    public Dictionary<string, string> Values { get; set; } = [];
}

public sealed class CreateRequestCommand
{
    /// <summary>Optional. Defaults to the catalogue item's name when a single item is ordered.</summary>
    public string? Title { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// Who the request is for. Null means the caller. Ordering for somebody else requires
    /// request.create only - the directory is already tenant-scoped, and a manager ordering for
    /// a new starter is the single most common request pattern.
    /// </summary>
    public Guid? RequestedForId { get; set; }

    public DateOnly? RequiredByDate { get; set; }

    public RequestChannel Channel { get; set; } = RequestChannel.Portal;

    public List<RequestLineInput> Items { get; set; } = [];
}

public sealed class UpdateRequestCommand
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public Priority? Priority { get; set; }
    public DateOnly? RequiredByDate { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class AssignRequestCommand
{
    public Guid? FulfilmentGroupId { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class ChangeRequestStatusCommand
{
    public RequestStatus Status { get; set; }
    public RequestPendingReason? PendingReason { get; set; }
    public string? Note { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class FulfilRequestItemCommand
{
    public string? Notes { get; set; }
}

public sealed class CancelRequestCommand
{
    public string? Reason { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class AddRequestCommentCommand
{
    public string Body { get; set; } = string.Empty;
    public IncidentCommentKind Kind { get; set; } = IncidentCommentKind.PublicComment;
}

public sealed class DecideApprovalCommand
{
    public bool Approved { get; set; }

    /// <summary>Mandatory when rejecting. Optional when approving.</summary>
    public string? Comment { get; set; }
}

/// <summary>Filters for the request queue. Mirrors the incident queue's shape.</summary>
public sealed class RequestQuery
{
    public string? Search { get; set; }
    public List<RequestStatus>? Statuses { get; set; }
    public List<Priority>? Priorities { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public Guid? FulfilmentGroupId { get; set; }
    public Guid? RequesterId { get; set; }
    public Guid? RequestedForId { get; set; }
    public bool? BreachedOnly { get; set; }
    public bool? OpenOnly { get; set; }

    /// <summary>Named scope, e.g. <c>my-work</c>, <c>awaiting-approval</c>, <c>unassigned</c>.</summary>
    public string? Scope { get; set; }

    public string SortBy { get; set; } = "createdAt";
    public bool SortDescending { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

/// <summary>Commands for editing the catalogue itself.</summary>
public sealed class UpsertCatalogItemCommand
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public Guid? FulfilmentGroupId { get; set; }
    public Priority Priority { get; set; } = Priority.P4Low;
    public bool RequiresApproval { get; set; }
    public ApprovalTargetKind ApprovalTargetKind { get; set; } = ApprovalTargetKind.Manager;
    public Guid? ApproverUserId { get; set; }
    public Guid? ApproverGroupId { get; set; }
    public ApprovalRule ApprovalRule { get; set; } = ApprovalRule.AnyOne;
    public decimal? Cost { get; set; }
    public int? EstimatedDeliveryDays { get; set; }
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public int? MaxQuantity { get; set; }
    public List<UpsertCatalogVariableCommand> Variables { get; set; } = [];
    public byte[]? RowVersion { get; set; }
}

public sealed class UpsertCatalogVariableCommand
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? HelpText { get; set; }
    public VariableType Type { get; set; } = VariableType.Text;
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public string? DefaultValue { get; set; }
    public List<string>? Choices { get; set; }
    public int? MaxLength { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
}
