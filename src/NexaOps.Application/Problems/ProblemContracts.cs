using NexaOps.Application.Common;
using NexaOps.Domain.Problems;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Application.Problems;

public sealed record ProblemSummaryDto(
    Guid Id,
    string Number,
    string Title,
    ProblemStatus Status,
    Priority Priority,
    ProblemOrigin Origin,
    Guid? AssignedToUserId,
    string? AssignedToName,
    string? AssignedToAvatarColor,
    Guid? OwnerUserId,
    string? OwnerName,
    Guid? AssignmentGroupId,
    string? AssignmentGroupName,
    Guid? CategoryId,
    string? CategoryName,
    bool HasWorkaround,
    int LinkedIncidentCount,
    bool IsMajorProblem,
    DateTimeOffset CreatedAt);

public sealed record ProblemCommentDto(
    Guid Id,
    IncidentCommentKind Kind,
    string Body,
    Guid AuthorId,
    string AuthorDisplayName,
    DateTimeOffset CreatedAt);

/// <param name="LinkedIncidents">Incidents this problem is believed to cause.</param>
public sealed record ProblemDetailDto(
    Guid Id,
    string Number,
    string Title,
    string Description,
    ProblemStatus Status,
    Priority Priority,
    ProblemOrigin Origin,
    Guid? CategoryId,
    string? CategoryName,
    Guid? SubcategoryId,
    string? SubcategoryName,
    Guid? AssignmentGroupId,
    string? AssignmentGroupName,
    Guid? AssignedToUserId,
    string? AssignedToName,
    Guid? OwnerUserId,
    string? OwnerName,
    string? RootCause,
    RootCauseConfidence? RootCauseConfidence,
    string? Workaround,
    string? PermanentFix,
    bool IsMajorProblem,
    int LinkedIncidentCount,
    DateTimeOffset? InvestigationStartedAt,
    DateTimeOffset? KnownErrorAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<LinkedIncidentDto> LinkedIncidents,
    IReadOnlyList<ProblemStatus> AllowedTransitions,
    byte[]? RowVersion);

public sealed record LinkedIncidentDto(
    Guid Id,
    string Number,
    string Title,
    IncidentStatus Status,
    Priority Priority,
    DateTimeOffset CreatedAt);

/// <summary>Counters for the problem management view.</summary>
public sealed record ProblemSummaryCountsDto(
    int OpenProblems,
    int Investigating,
    int KnownErrors,
    int AssignedToMe,
    int OwnedByMe,
    int Unassigned,
    int MajorProblems,
    IReadOnlyList<ProblemStatusCountDto> OpenByStatus);

public sealed record ProblemStatusCountDto(ProblemStatus Status, int Count);

// ---------------------------------------------------------------------------
// Commands
// ---------------------------------------------------------------------------

public sealed class CreateProblemCommand
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? SubcategoryId { get; set; }
    public Priority Priority { get; set; } = Priority.P3Moderate;
    public ProblemOrigin Origin { get; set; } = ProblemOrigin.FromIncident;

    /// <summary>
    /// Incident this problem was raised from. Linked immediately, because a problem raised in
    /// isolation loses the evidence that justified raising it.
    /// </summary>
    public Guid? FromIncidentId { get; set; }
}

public sealed class UpdateProblemCommand
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? SubcategoryId { get; set; }
    public Priority? Priority { get; set; }
    public bool? IsMajorProblem { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class RecordFindingsCommand
{
    public string? RootCause { get; set; }
    public RootCauseConfidence? Confidence { get; set; }
    public string? Workaround { get; set; }
    public string? PermanentFix { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class AssignProblemCommand
{
    public Guid? AssignmentGroupId { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class ChangeProblemStatusCommand
{
    public ProblemStatus Status { get; set; }
    public string? Note { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class AddProblemCommentCommand
{
    public string Body { get; set; } = string.Empty;
    public IncidentCommentKind Kind { get; set; } = IncidentCommentKind.WorkNote;
}

public sealed class LinkIncidentCommand
{
    public Guid IncidentId { get; set; }
}

public sealed class ProblemQuery
{
    public string? Search { get; set; }
    public List<ProblemStatus>? Statuses { get; set; }
    public List<Priority>? Priorities { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public Guid? CategoryId { get; set; }
    public bool? OpenOnly { get; set; }
    public bool? KnownErrorsOnly { get; set; }

    /// <summary>Named scope, e.g. <c>my-work</c>, <c>known-errors</c>, <c>unassigned</c>.</summary>
    public string? Scope { get; set; }

    public string SortBy { get; set; } = "createdAt";
    public bool SortDescending { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

/// <summary>Write-side access to problem aggregates.</summary>
public interface IProblemRepository
{
    Task<Problem?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Problem?> GetByNumberAsync(string number, CancellationToken cancellationToken = default);

    void Add(Problem problem);

    void AddComment(ProblemComment comment);

    /// <summary>
    /// Declares the row version the caller read, so a concurrent change is detected. Lives in
    /// the port because assigning the entity property does nothing - EF compares the version the
    /// change tracker loaded.
    /// </summary>
    void SetExpectedVersion(Problem problem, byte[] rowVersion);

    /// <summary>Refreshes the denormalised count of incidents attributed to this problem.</summary>
    Task RefreshLinkedIncidentCountAsync(Guid problemId, CancellationToken cancellationToken = default);
}

/// <summary>Read-side queries for problems.</summary>
public interface IProblemQueryService
{
    Task<PagedResult<ProblemSummaryDto>> SearchAsync(
        ProblemQuery query,
        CancellationToken cancellationToken = default);

    Task<ProblemDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ProblemDetailDto?> GetByNumberAsync(string number, CancellationToken cancellationToken = default);

    /// <summary>Null when the caller cannot see the problem, which the service turns into a 404.</summary>
    Task<IReadOnlyList<ProblemCommentDto>?> GetCommentsAsync(
        Guid problemId,
        CancellationToken cancellationToken = default);

    Task<ProblemSummaryCountsDto> GetSummaryAsync(CancellationToken cancellationToken = default);
}
