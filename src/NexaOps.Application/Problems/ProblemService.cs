using FluentValidation;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Incidents;
using NexaOps.Application.Security;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Common;
using NexaOps.Domain.Problems;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Application.Problems;

/// <summary>
/// The only way a problem changes.
/// <para>
/// Permission checks live here as well as on the endpoint, because publishing a known error and
/// resolving a problem are also reachable from the workflow engine later.
/// </para>
/// </summary>
public interface IProblemService
{
    Task<PagedResult<ProblemSummaryDto>> SearchAsync(ProblemQuery query, CancellationToken ct = default);

    Task<ProblemDetailDto> GetAsync(Guid id, CancellationToken ct = default);

    Task<ProblemDetailDto> GetByNumberAsync(string number, CancellationToken ct = default);

    Task<ProblemSummaryCountsDto> GetSummaryAsync(CancellationToken ct = default);

    Task<ProblemDetailDto> CreateAsync(CreateProblemCommand command, CancellationToken ct = default);

    Task<ProblemDetailDto> UpdateAsync(Guid id, UpdateProblemCommand command, CancellationToken ct = default);

    Task<ProblemDetailDto> RecordFindingsAsync(Guid id, RecordFindingsCommand command, CancellationToken ct = default);

    Task<ProblemDetailDto> AssignAsync(Guid id, AssignProblemCommand command, CancellationToken ct = default);

    Task<ProblemDetailDto> ChangeStatusAsync(Guid id, ChangeProblemStatusCommand command, CancellationToken ct = default);

    Task<IReadOnlyList<ProblemCommentDto>> GetCommentsAsync(Guid id, CancellationToken ct = default);

    Task<ProblemCommentDto> AddCommentAsync(Guid id, AddProblemCommentCommand command, CancellationToken ct = default);

    Task<ProblemDetailDto> LinkIncidentAsync(Guid id, LinkIncidentCommand command, CancellationToken ct = default);

    Task<ProblemDetailDto> UnlinkIncidentAsync(Guid id, Guid incidentId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class ProblemService : IProblemService
{
    private const string EntityType = nameof(Problem);
    private const string NumberSequenceKey = "PRB";

    private static readonly string[] SortableFields =
        ["createdAt", "number", "priority", "status", "linkedIncidentCount"];

    private readonly IProblemRepository _problems;
    private readonly IProblemQueryService _queries;
    private readonly IIncidentRepository _incidents;
    private readonly IServiceDeskReferenceRepository _reference;
    private readonly INumberSequenceService _numbers;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ProblemService> _logger;

    public ProblemService(
        IProblemRepository problems,
        IProblemQueryService queries,
        IIncidentRepository incidents,
        IServiceDeskReferenceRepository reference,
        INumberSequenceService numbers,
        IAuditService audit,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<ProblemService> logger)
    {
        _problems = problems;
        _queries = queries;
        _incidents = incidents;
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
    public Task<PagedResult<ProblemSummaryDto>> SearchAsync(ProblemQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        _currentUser.DemandPermission(Permissions.ProblemRead);

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
    public async Task<ProblemDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.ProblemRead);

        return await _queries.GetAsync(id, ct).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(EntityType, id);
    }

    /// <inheritdoc />
    public async Task<ProblemDetailDto> GetByNumberAsync(string number, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.ProblemRead);

        return await _queries.GetByNumberAsync(number, ct).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(EntityType, number);
    }

    /// <inheritdoc />
    public Task<ProblemSummaryCountsDto> GetSummaryAsync(CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.ProblemRead);
        return _queries.GetSummaryAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProblemCommentDto>> GetCommentsAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.ProblemRead);

        return await _queries.GetCommentsAsync(id, ct).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(EntityType, id);
    }

    // -----------------------------------------------------------------
    // Writes
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<ProblemDetailDto> CreateAsync(CreateProblemCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.ProblemCreate);

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

        Incident? source = null;

        if (command.FromIncidentId is not null)
        {
            // Tenant-filtered, so an incident from a neighbouring tenant reads as non-existent.
            source = await _incidents.GetAsync(command.FromIncidentId.Value, ct).ConfigureAwait(false)
                     ?? throw new EntityNotFoundException(nameof(Incident), command.FromIncidentId.Value);
        }

        var number = await _numbers.NextAsync(NumberSequenceKey, ct).ConfigureAwait(false);

        var problem = new Problem
        {
            Number = number,
            Title = command.Title.Trim(),
            Description = command.Description?.Trim() ?? string.Empty,

            // Classification defaults from the originating incident, which is almost always what
            // the investigator wants and saves re-entering it.
            CategoryId = command.CategoryId ?? source?.CategoryId,
            SubcategoryId = command.SubcategoryId ?? source?.SubcategoryId,
            Priority = command.Priority,
            Origin = command.Origin,
            OwnerUserId = _currentUser.UserId,
            Status = ProblemStatus.New
        };

        _problems.Add(problem);

        if (source is not null)
        {
            source.ProblemId = problem.Id;
            problem.LinkedIncidentCount = 1;
        }

        _audit.Record(
            AuditAction.Create,
            EntityType,
            problem.Id.ToString(),
            problem.Number,
            source is null
                ? $"Raised {problem.Number}."
                : $"Raised {problem.Number} from incident {source.Number}.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Problem {Number} raised by {UserId} from {Origin}.",
            problem.Number, _currentUser.UserId, problem.Origin);

        return await ReloadAsync(problem.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProblemDetailDto> UpdateAsync(Guid id, UpdateProblemCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.ProblemUpdate);

        var problem = await LoadAsync(id, ct).ConfigureAwait(false);
        ApplyConcurrencyToken(problem, command.RowVersion);

        if (!string.IsNullOrWhiteSpace(command.Title))
        {
            problem.Title = command.Title.Trim();
        }

        if (command.Description is not null)
        {
            problem.Description = command.Description.Trim();
        }

        if (command.CategoryId is not null)
        {
            _ = await _reference.GetCategoryAsync(command.CategoryId.Value, ct).ConfigureAwait(false)
                ?? throw new EntityNotFoundException("Category", command.CategoryId.Value);

            problem.CategoryId = command.CategoryId;
        }

        if (command.SubcategoryId is not null)
        {
            _ = await _reference.GetSubcategoryAsync(command.SubcategoryId.Value, ct).ConfigureAwait(false)
                ?? throw new EntityNotFoundException("Subcategory", command.SubcategoryId.Value);

            problem.SubcategoryId = command.SubcategoryId;
        }

        if (command.Priority is not null)
        {
            problem.Priority = command.Priority.Value;
        }

        if (command.IsMajorProblem is not null)
        {
            problem.IsMajorProblem = command.IsMajorProblem.Value;
        }

        _audit.Record(AuditAction.Update, EntityType, problem.Id.ToString(), problem.Number, "Problem updated.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(problem.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProblemDetailDto> RecordFindingsAsync(
        Guid id,
        RecordFindingsCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.ProblemInvestigate);

        var problem = await LoadAsync(id, ct).ConfigureAwait(false);
        ApplyConcurrencyToken(problem, command.RowVersion);

        problem.RecordFindings(command.RootCause, command.Confidence, command.Workaround);

        if (!string.IsNullOrWhiteSpace(command.PermanentFix))
        {
            problem.PermanentFix = command.PermanentFix.Trim();
        }

        // Recording findings on an untouched problem starts the investigation, so an investigator
        // does not have to set the status separately before typing what they found.
        if (problem.Status == ProblemStatus.New)
        {
            problem.TransitionTo(ProblemStatus.Investigating, _currentUser.UserId, _clock.UtcNow);
        }

        _audit.Record(
            AuditAction.Update,
            EntityType,
            problem.Id.ToString(),
            problem.Number,
            "Investigation findings recorded.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(problem.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProblemDetailDto> AssignAsync(Guid id, AssignProblemCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.ProblemAssign);

        var problem = await LoadAsync(id, ct).ConfigureAwait(false);
        ApplyConcurrencyToken(problem, command.RowVersion);

        if (command.AssignmentGroupId is not null
            && !await _reference.GroupExistsAsync(command.AssignmentGroupId.Value, ct).ConfigureAwait(false))
        {
            throw new EntityNotFoundException("Group", command.AssignmentGroupId.Value);
        }

        foreach (var userId in new[] { command.AssignedToUserId, command.OwnerUserId })
        {
            if (userId is not null && !await _reference.UserExistsAsync(userId.Value, ct).ConfigureAwait(false))
            {
                throw new EntityNotFoundException("User", userId.Value);
            }
        }

        problem.Assign(command.AssignmentGroupId, command.AssignedToUserId, command.OwnerUserId);

        _audit.Record(AuditAction.Assign, EntityType, problem.Id.ToString(), problem.Number, "Assignment changed.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(problem.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProblemDetailDto> ChangeStatusAsync(
        Guid id,
        ChangeProblemStatusCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Publishing a known error commits the whole service desk to a workaround, and resolving
        // asserts the cause is gone. Both are deliberately separate from a general edit.
        _currentUser.DemandPermission(command.Status switch
        {
            ProblemStatus.KnownError => Permissions.ProblemPublishKnownError,
            ProblemStatus.Resolved => Permissions.ProblemResolve,
            ProblemStatus.Closed => Permissions.ProblemClose,
            ProblemStatus.Cancelled => Permissions.ProblemCancel,
            _ => Permissions.ProblemUpdate
        });

        var problem = await LoadAsync(id, ct).ConfigureAwait(false);
        ApplyConcurrencyToken(problem, command.RowVersion);

        var previous = problem.Status;
        problem.TransitionTo(command.Status, _currentUser.UserId, _clock.UtcNow);

        _audit.Record(
            AuditAction.Update,
            EntityType,
            problem.Id.ToString(),
            problem.Number,
            $"Status changed from {previous} to {problem.Status}." +
            (string.IsNullOrWhiteSpace(command.Note) ? string.Empty : $" {command.Note.Trim()}"));

        if (problem.Status == ProblemStatus.KnownError)
        {
            _logger.LogInformation(
                "Problem {Number} published as a known error with a workaround.", problem.Number);
        }

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(problem.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProblemCommentDto> AddCommentAsync(
        Guid id,
        AddProblemCommentCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.ProblemCommentCreate);

        if (string.IsNullOrWhiteSpace(command.Body))
        {
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(nameof(command.Body), "A comment cannot be empty.")
            ]);
        }

        var problem = await LoadAsync(id, ct).ConfigureAwait(false);

        var comment = new ProblemComment
        {
            ProblemId = problem.Id,
            Kind = command.Kind,
            Body = command.Body.Trim(),
            AuthorId = _currentUser.UserId,
            AuthorDisplayName = _currentUser.DisplayName
        };

        _problems.AddComment(comment);

        _audit.Record(AuditAction.Update, EntityType, problem.Id.ToString(), problem.Number, "Comment added.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return new ProblemCommentDto(
            comment.Id, comment.Kind, comment.Body, comment.AuthorId,
            comment.AuthorDisplayName, comment.CreatedAt);
    }

    /// <inheritdoc />
    public async Task<ProblemDetailDto> LinkIncidentAsync(
        Guid id,
        LinkIncidentCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.ProblemLinkIncident);

        var problem = await LoadAsync(id, ct).ConfigureAwait(false);

        var incident = await _incidents.GetAsync(command.IncidentId, ct).ConfigureAwait(false)
                       ?? throw new EntityNotFoundException(nameof(Incident), command.IncidentId);

        if (incident.ProblemId == problem.Id)
        {
            // Already linked. Idempotent rather than an error: a double-click must not fail.
            return await ReloadAsync(problem.Id, ct).ConfigureAwait(false);
        }

        incident.ProblemId = problem.Id;

        _audit.Record(
            AuditAction.Update,
            EntityType,
            problem.Id.ToString(),
            problem.Number,
            $"Linked incident {incident.Number}.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        await _problems.RefreshLinkedIncidentCountAsync(problem.Id, ct).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await ReloadAsync(problem.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProblemDetailDto> UnlinkIncidentAsync(Guid id, Guid incidentId, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.ProblemLinkIncident);

        var problem = await LoadAsync(id, ct).ConfigureAwait(false);

        var incident = await _incidents.GetAsync(incidentId, ct).ConfigureAwait(false)
                       ?? throw new EntityNotFoundException(nameof(Incident), incidentId);

        if (incident.ProblemId != problem.Id)
        {
            throw new DomainException(
                "problem.incident_not_linked",
                "That incident is not linked to this problem.");
        }

        incident.ProblemId = null;

        _audit.Record(
            AuditAction.Update,
            EntityType,
            problem.Id.ToString(),
            problem.Number,
            $"Unlinked incident {incident.Number}.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        await _problems.RefreshLinkedIncidentCountAsync(problem.Id, ct).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await ReloadAsync(problem.Id, ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private async Task<Problem> LoadAsync(Guid id, CancellationToken ct)
        => await _problems.GetAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(EntityType, id);

    private async Task<ProblemDetailDto> ReloadAsync(Guid id, CancellationToken ct)
        => await _queries.GetAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(EntityType, id);

    private void ApplyConcurrencyToken(Problem problem, byte[]? rowVersion)
    {
        if (rowVersion is { Length: > 0 })
        {
            _problems.SetExpectedVersion(problem, rowVersion);
        }
    }
}
