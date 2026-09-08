using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Notifications;
using NexaOps.Application.Security;
using NexaOps.Application.Sla;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Common;
using NexaOps.Domain.Platform;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Sla;

namespace NexaOps.Application.Incidents;

/// <inheritdoc />
public sealed class IncidentService : IIncidentService
{
    private const string EntityType = nameof(Incident);
    private const string NumberSequenceKey = "INC";

    private readonly IIncidentRepository _incidents;
    private readonly IIncidentQueryService _queries;
    private readonly IServiceDeskReferenceRepository _reference;
    private readonly INumberSequenceService _numbers;
    private readonly ISlaService _sla;
    private readonly INotificationService _notifications;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenant;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<IncidentService> _logger;

    public IncidentService(
        IIncidentRepository incidents,
        IIncidentQueryService queries,
        IServiceDeskReferenceRepository reference,
        INumberSequenceService numbers,
        ISlaService sla,
        INotificationService notifications,
        IAuditService audit,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ITenantContext tenant,
        IDateTimeProvider clock,
        ILogger<IncidentService> logger)
    {
        _incidents = incidents;
        _queries = queries;
        _reference = reference;
        _numbers = numbers;
        _sla = sla;
        _notifications = notifications;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _tenant = tenant;
        _clock = clock;
        _logger = logger;
    }

    // -----------------------------------------------------------------
    // Reads
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public Task<PagedResult<IncidentListItemDto>> SearchAsync(
        IncidentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        _currentUser.DemandPermission(Permissions.IncidentRead);

        if (query.IncludeArchived && !_currentUser.HasPermission(Permissions.IncidentArchive))
        {
            query.IncludeArchived = false;
        }

        if (query.SortBy is not null && !IncidentQuery.SortableFields.Contains(query.SortBy))
        {
            // A bad request, not a conflict: the caller sent something the API does not accept.
            // Reported per-field so a client can highlight the offending parameter.
            throw new FluentValidation.ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(IncidentQuery.SortBy),
                    $"'{query.SortBy}' is not a sortable field. Supported fields: " +
                    string.Join(", ", IncidentQuery.SortableFields.Order(StringComparer.Ordinal)) + ".")
            ]);
        }

        return _queries.SearchAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IncidentDetailDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _currentUser.DemandPermission(Permissions.IncidentRead);

        var detail = await _queries.GetDetailAsync(id, cancellationToken).ConfigureAwait(false);
        return detail ?? throw new EntityNotFoundException(EntityType, id);
    }

    /// <inheritdoc />
    public async Task<IncidentDetailDto> GetByNumberAsync(
        string number,
        CancellationToken cancellationToken = default)
    {
        _currentUser.DemandPermission(Permissions.IncidentRead);

        var incident = await _incidents.GetByNumberAsync(number, cancellationToken).ConfigureAwait(false)
                       ?? throw new EntityNotFoundException(EntityType, number);

        return await GetAsync(incident.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IncidentCommentDto>> GetCommentsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await EnsureVisibleAsync(id, cancellationToken).ConfigureAwait(false);
        return await _queries.GetCommentsAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IncidentActivityDto>> GetActivityAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await EnsureVisibleAsync(id, cancellationToken).ConfigureAwait(false);
        return await _queries.GetActivityAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<ServiceDeskSummaryDto> GetServiceDeskSummaryAsync(CancellationToken cancellationToken = default)
    {
        _currentUser.DemandPermission(Permissions.IncidentRead);
        return _queries.GetServiceDeskSummaryAsync(cancellationToken);
    }

    // -----------------------------------------------------------------
    // Create
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<IncidentDetailDto> CreateAsync(
        CreateIncidentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.IncidentCreate);

        var actorId = _currentUser.UserId;
        var requesterId = command.RequesterId ?? actorId;

        // Raising a ticket on behalf of someone else is an agent action, not an employee one.
        if (requesterId != actorId)
        {
            _currentUser.DemandPermission(Permissions.IncidentUpdate);

            if (!await _reference.UserExistsAsync(requesterId, cancellationToken).ConfigureAwait(false))
            {
                throw new EntityNotFoundException("User", requesterId);
            }
        }

        await ValidateClassificationAsync(command.CategoryId, command.SubcategoryId, cancellationToken)
            .ConfigureAwait(false);

        if (command.AssignmentGroupId is not null || command.AssignedToUserId is not null)
        {
            _currentUser.DemandPermission(Permissions.IncidentAssign);
            await ValidateAssignmentAsync(command.AssignmentGroupId, command.AssignedToUserId, cancellationToken)
                .ConfigureAwait(false);
        }

        var (organizationId, departmentId) = await _reference
            .GetUserPlacementAsync(requesterId, cancellationToken)
            .ConfigureAwait(false);

        var matrix = await _reference.GetPriorityMatrixAsync(cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;

        // Number allocation locks a counter row, so it and the insert must share a transaction.
        var incidentId = await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var number = await _numbers.NextAsync(NumberSequenceKey, ct).ConfigureAwait(false);

            var incident = new Incident
            {
                TenantId = _tenant.TenantId,
                Number = number,
                Title = command.Title.Trim(),
                Description = command.Description.Trim(),
                RequesterId = requesterId,
                AffectedUserId = command.AffectedUserId ?? requesterId,
                OrganizationId = organizationId,
                DepartmentId = departmentId,
                CategoryId = command.CategoryId,
                SubcategoryId = command.SubcategoryId,
                Impact = command.Impact,
                Urgency = command.Urgency,
                Priority = PriorityCalculator.Resolve(matrix, command.Impact, command.Urgency),
                Channel = command.Channel,
                Status = IncidentStatus.New,
                ParentIncidentId = command.ParentIncidentId,
                CreatedAt = now
            };

            // Routing falls back through subcategory, then category, then nothing at all -
            // an unrouted incident lands in the unassigned queue rather than being guessed at.
            var groupId = command.AssignmentGroupId
                          ?? await ResolveDefaultGroupAsync(command.SubcategoryId, command.CategoryId, ct)
                              .ConfigureAwait(false);

            incident.Assign(groupId, command.AssignedToUserId);

            _incidents.Add(incident);
            ApplyTags(incident, command.Tags);

            await _sla.AttachClocksAsync(incident, ct).ConfigureAwait(false);

            _audit.Record(
                AuditAction.Create,
                EntityType,
                incident.Id.ToString(),
                incident.Number,
                $"Incident raised via {incident.Channel}.");

            await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

            NotifyOnAssignment(incident, previousAssignee: null);

            await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            return incident.Id;
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Incident {IncidentId} created by {UserId}.", incidentId, actorId);

        return await GetAsync(incidentId, cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Update
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<IncidentDetailDto> UpdateAsync(
        Guid id,
        UpdateIncidentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.IncidentUpdate);

        var incident = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        EnsureNotTerminal(incident);
        ApplyConcurrencyToken(incident, command.ConcurrencyToken);

        await ValidateClassificationAsync(command.CategoryId, command.SubcategoryId, cancellationToken)
            .ConfigureAwait(false);

        var priorityInputsChanged = false;

        if (command.Title is not null)
        {
            incident.Title = command.Title.Trim();
        }

        if (command.Description is not null)
        {
            incident.Description = command.Description.Trim();
        }

        if (command.CategoryId is not null)
        {
            incident.CategoryId = command.CategoryId;
        }

        if (command.SubcategoryId is not null)
        {
            incident.SubcategoryId = command.SubcategoryId;
        }

        if (command.AffectedUserId is not null)
        {
            if (!await _reference.UserExistsAsync(command.AffectedUserId.Value, cancellationToken).ConfigureAwait(false))
            {
                throw new EntityNotFoundException("User", command.AffectedUserId.Value);
            }

            incident.AffectedUserId = command.AffectedUserId;
        }

        if (command.OrganizationId is not null)
        {
            incident.OrganizationId = command.OrganizationId;
        }

        if (command.DepartmentId is not null)
        {
            incident.DepartmentId = command.DepartmentId;
        }

        if (command.Impact is not null && command.Impact != incident.Impact)
        {
            incident.Impact = command.Impact.Value;
            priorityInputsChanged = true;
        }

        if (command.Urgency is not null && command.Urgency != incident.Urgency)
        {
            incident.Urgency = command.Urgency.Value;
            priorityInputsChanged = true;
        }

        if (priorityInputsChanged)
        {
            var matrix = await _reference.GetPriorityMatrixAsync(cancellationToken).ConfigureAwait(false);
            var previousPriority = incident.Priority;

            incident.ApplyDerivedPriority(
                PriorityCalculator.Resolve(matrix, incident.Impact, incident.Urgency));

            if (incident.Priority != previousPriority)
            {
                await _sla.OnPriorityChangedAsync(incident, cancellationToken).ConfigureAwait(false);
                NotifyPriorityChanged(incident, previousPriority);
            }
        }

        if (command.Tags is not null)
        {
            _incidents.RemoveTags(incident.Tags.ToList());
            incident.Tags.Clear();
            ApplyTags(incident, command.Tags);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Assignment
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<IncidentDetailDto> AssignAsync(
        Guid id,
        AssignIncidentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.IncidentAssign);

        var incident = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        await ValidateAssignmentAsync(command.AssignmentGroupId, command.AssignedToUserId, cancellationToken)
            .ConfigureAwait(false);

        var previousAssignee = incident.AssignedToUserId;
        var previousGroup = incident.AssignmentGroupId;

        incident.Assign(command.AssignmentGroupId, command.AssignedToUserId);

        if (!string.IsNullOrWhiteSpace(command.Note))
        {
            AddSystemNote(incident, command.Note.Trim(), IncidentCommentKind.WorkNote);
        }

        _audit.Record(
            AuditAction.Assign,
            EntityType,
            incident.Id.ToString(),
            incident.Number,
            $"Assignment changed from group {previousGroup} / user {previousAssignee} " +
            $"to group {incident.AssignmentGroupId} / user {incident.AssignedToUserId}.");

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        NotifyOnAssignment(incident, previousAssignee);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<IncidentDetailDto> ChangeStatusAsync(
        Guid id,
        ChangeIncidentStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        DemandStatusPermission(command.Status);

        var incident = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        var previousStatus = incident.Status;

        // Reopening is a transition from Resolved back into active work.
        if (previousStatus == IncidentStatus.Resolved && command.Status == IncidentStatus.InProgress)
        {
            _currentUser.DemandPermission(Permissions.IncidentReopen);

            if (string.IsNullOrWhiteSpace(command.Notes))
            {
                throw new DomainException(
                    "incident.reopen_reason_required",
                    "A reason is required when reopening a resolved incident.");
            }
        }

        if (command.Status == IncidentStatus.Pending)
        {
            if (command.PendingReason is null)
            {
                throw new DomainException(
                    "incident.pending_reason_required",
                    "A pending reason is required so that the paused SLA is explainable.");
            }

            incident.PendingReason = command.PendingReason;
        }

        if (command.Status == IncidentStatus.Resolved)
        {
            if (command.ResolutionCode is null || string.IsNullOrWhiteSpace(command.Notes))
            {
                throw new DomainException(
                    "incident.resolution_required",
                    "A resolution code and resolution notes are required to resolve an incident.");
            }

            incident.ResolutionCode = command.ResolutionCode;
            incident.ResolutionNotes = command.Notes.Trim();
        }

        incident.TransitionTo(command.Status, _currentUser.UserId, _clock.UtcNow);

        // The incident lifecycle is translated into the engine's own vocabulary here, so the SLA
        // rules stay identical across modules while each module keeps its own statuses.
        await _sla.OnStatusChangedAsync(
                incident,
                IncidentSlaMapping.For(incident, previousStatus),
                cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(command.Notes) && command.Status != IncidentStatus.Resolved)
        {
            AddSystemNote(incident, command.Notes.Trim(), IncidentCommentKind.WorkNote);
        }

        _audit.Record(
            AuditAction.StatusChange,
            EntityType,
            incident.Id.ToString(),
            incident.Number,
            $"Status changed from {previousStatus} to {incident.Status}.");

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        NotifyStatusChanged(incident, previousStatus);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IncidentDetailDto> ChangePriorityAsync(
        Guid id,
        ChangeIncidentPriorityCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.IncidentUpdate);

        var incident = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        EnsureNotTerminal(incident);

        var previousPriority = incident.Priority;

        if (command.Impact is not null)
        {
            incident.Impact = command.Impact.Value;
        }

        if (command.Urgency is not null)
        {
            incident.Urgency = command.Urgency.Value;
        }

        if (command.OverridePriority is not null)
        {
            _currentUser.DemandPermission(Permissions.IncidentPriorityOverride);
            incident.OverridePriority(command.OverridePriority.Value, command.OverrideReason ?? string.Empty);
        }
        else
        {
            // Clearing the override returns the incident to the tenant matrix.
            incident.IsPriorityOverridden = false;
            incident.PriorityOverrideReason = null;

            var matrix = await _reference.GetPriorityMatrixAsync(cancellationToken).ConfigureAwait(false);
            incident.ApplyDerivedPriority(
                PriorityCalculator.Resolve(matrix, incident.Impact, incident.Urgency));
        }

        if (incident.Priority != previousPriority)
        {
            await _sla.OnPriorityChangedAsync(incident, cancellationToken).ConfigureAwait(false);

            _audit.Record(
                AuditAction.Update,
                EntityType,
                incident.Id.ToString(),
                incident.Number,
                $"Priority changed from {previousPriority} to {incident.Priority}" +
                (incident.IsPriorityOverridden ? $" (override: {incident.PriorityOverrideReason})" : "."));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (incident.Priority != previousPriority)
        {
            NotifyPriorityChanged(incident, previousPriority);
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IncidentDetailDto> DeclareMajorAsync(
        Guid id,
        DeclareMajorIncidentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.IncidentDeclareMajor);

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new DomainException(
                "incident.major_reason_required",
                "A justification is required when declaring or withdrawing a major incident.");
        }

        var incident = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        EnsureNotTerminal(incident);

        incident.IsMajorIncident = command.IsMajorIncident;

        var verb = command.IsMajorIncident ? "declared" : "withdrawn";
        AddSystemNote(
            incident,
            $"Major incident {verb}. Reason: {command.Reason.Trim()}",
            IncidentCommentKind.WorkNote);

        _audit.Record(
            AuditAction.Update,
            EntityType,
            incident.Id.ToString(),
            incident.Number,
            $"Major incident {verb}: {command.Reason.Trim()}");

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Comments
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<IncidentCommentDto> AddCommentAsync(
        Guid id,
        AddIncidentCommentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        _currentUser.DemandPermission(
            command.Kind == IncidentCommentKind.WorkNote
                ? Permissions.IncidentWorkNoteCreate
                : Permissions.IncidentCommentCreate);

        await EnsureVisibleAsync(id, cancellationToken).ConfigureAwait(false);

        var incident = await LoadAsync(id, cancellationToken).ConfigureAwait(false);

        if (incident.Status == IncidentStatus.Closed)
        {
            throw new DomainException(
                "incident.closed_no_comments",
                "A closed incident cannot receive new comments. Raise a new incident that references it.");
        }

        var now = _clock.UtcNow;
        var comment = new IncidentComment
        {
            TenantId = _tenant.TenantId,
            IncidentId = incident.Id,
            Kind = command.Kind,
            Body = command.Body.Trim(),
            AuthorUserId = _currentUser.UserId,
            IsSystemGenerated = false,
            CreatedAt = now
        };

        _incidents.AddComment(comment);

        // A public reply from someone other than the requester is the first agent response.
        if (command.Kind == IncidentCommentKind.PublicComment
            && incident.FirstRespondedAt is null
            && _currentUser.UserId != incident.RequesterId)
        {
            incident.RecordFirstResponse(now);
            await _sla.OnFirstResponseAsync(incident, now, cancellationToken).ConfigureAwait(false);
        }

        _audit.Record(
            AuditAction.Comment,
            EntityType,
            incident.Id.ToString(),
            incident.Number,
            $"{command.Kind} added.");

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        NotifyCommented(incident, comment);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var comments = await _queries.GetCommentsAsync(id, cancellationToken).ConfigureAwait(false);
        return comments.First(c => c.Id == comment.Id);
    }

    // -----------------------------------------------------------------
    // Archive
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task ArchiveAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        _currentUser.DemandPermission(Permissions.IncidentArchive);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                "incident.archive_reason_required",
                "A reason is required when archiving an incident.");
        }

        var incident = await LoadAsync(id, cancellationToken).ConfigureAwait(false);

        incident.IsArchived = true;
        incident.ArchivedAt = _clock.UtcNow;
        incident.ArchivedBy = _currentUser.UserId;

        foreach (var clock in incident.SlaInstances)
        {
            clock.Cancel();
        }

        _audit.Record(
            AuditAction.Archive,
            EntityType,
            incident.Id.ToString(),
            incident.Number,
            $"Archived: {reason.Trim()}");

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private async Task<Incident> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        await EnsureVisibleAsync(id, cancellationToken).ConfigureAwait(false);

        return await _incidents.GetWithClocksAsync(id, cancellationToken).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(EntityType, id);
    }

    /// <summary>
    /// Confirms the caller may see this incident at all. Someone without
    /// <c>incident.read.all</c> may only touch incidents they are involved in, and the check is
    /// a query against the database rather than a filter over an already-loaded row, so an
    /// invisible incident is indistinguishable from one that does not exist.
    /// </summary>
    private async Task EnsureVisibleAsync(Guid id, CancellationToken cancellationToken)
    {
        _currentUser.DemandPermission(Permissions.IncidentRead);

        if (!await _queries.CanViewAsync(id, cancellationToken).ConfigureAwait(false))
        {
            throw new EntityNotFoundException(EntityType, id);
        }
    }

    private void DemandStatusPermission(IncidentStatus status)
    {
        var permission = status switch
        {
            IncidentStatus.Resolved => Permissions.IncidentResolve,
            IncidentStatus.Closed => Permissions.IncidentClose,
            IncidentStatus.Cancelled => Permissions.IncidentCancel,
            _ => Permissions.IncidentUpdate
        };

        _currentUser.DemandPermission(permission);
    }

    private static void EnsureNotTerminal(Incident incident)
    {
        if (IncidentStateMachine.IsTerminal(incident.Status))
        {
            throw new DomainException(
                "incident.terminal_readonly",
                $"A {incident.Status} incident cannot be modified.");
        }
    }

    /// <summary>
    /// Attaches the caller's row version so a concurrent edit is rejected with 409 rather than
    /// silently overwriting a colleague's change.
    /// </summary>
    private void ApplyConcurrencyToken(Incident incident, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        byte[] rowVersion;
        try
        {
            rowVersion = Convert.FromBase64String(token);
        }
        catch (FormatException)
        {
            throw new ConflictException(
                "incident.invalid_concurrency_token",
                "The supplied concurrency token is not valid. Reload the incident and try again.");
        }

        _incidents.SetExpectedVersion(incident, rowVersion);
    }

    private async Task ValidateClassificationAsync(
        Guid? categoryId,
        Guid? subcategoryId,
        CancellationToken cancellationToken)
    {
        if (categoryId is not null)
        {
            var category = await _reference.GetCategoryAsync(categoryId.Value, cancellationToken)
                .ConfigureAwait(false);

            if (category is null || category.Module != ServiceModule.Incident)
            {
                throw new EntityNotFoundException(nameof(Category), categoryId.Value);
            }
        }

        if (subcategoryId is null)
        {
            return;
        }

        var subcategory = await _reference.GetSubcategoryAsync(subcategoryId.Value, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(Subcategory), subcategoryId.Value);

        if (categoryId is not null && subcategory.CategoryId != categoryId)
        {
            throw new DomainException(
                "incident.subcategory_mismatch",
                "The selected subcategory does not belong to the selected category.");
        }
    }

    private async Task ValidateAssignmentAsync(
        Guid? groupId,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (groupId is not null
            && !await _reference.GroupExistsAsync(groupId.Value, cancellationToken).ConfigureAwait(false))
        {
            throw new EntityNotFoundException("Group", groupId.Value);
        }

        if (userId is null)
        {
            return;
        }

        if (!await _reference.UserExistsAsync(userId.Value, cancellationToken).ConfigureAwait(false))
        {
            throw new EntityNotFoundException("User", userId.Value);
        }

        // Assigning work to someone outside the owning group is almost always a mistake, and
        // it makes team workload reporting meaningless. Reject it explicitly.
        if (groupId is not null
            && !await _reference.IsMemberOfGroupAsync(userId.Value, groupId.Value, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new DomainException(
                "incident.assignee_not_in_group",
                "The selected assignee is not a member of the selected assignment group.");
        }
    }

    private async Task<Guid?> ResolveDefaultGroupAsync(
        Guid? subcategoryId,
        Guid? categoryId,
        CancellationToken cancellationToken)
    {
        if (subcategoryId is not null)
        {
            var subcategory = await _reference.GetSubcategoryAsync(subcategoryId.Value, cancellationToken)
                .ConfigureAwait(false);

            if (subcategory?.DefaultAssignmentGroupId is not null)
            {
                return subcategory.DefaultAssignmentGroupId;
            }
        }

        if (categoryId is null)
        {
            return null;
        }

        var category = await _reference.GetCategoryAsync(categoryId.Value, cancellationToken)
            .ConfigureAwait(false);

        return category?.DefaultAssignmentGroupId;
    }

    private void ApplyTags(Incident incident, IReadOnlyList<string>? tags)
    {
        if (tags is null)
        {
            return;
        }

        var normalised = tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Take(20);

        foreach (var tag in normalised)
        {
            var entity = new IncidentTag
            {
                TenantId = incident.TenantId,
                IncidentId = incident.Id,
                Tag = tag,
                CreatedAt = _clock.UtcNow
            };

            incident.Tags.Add(entity);
            _incidents.AddTag(entity);
        }
    }

    private void AddSystemNote(Incident incident, string body, IncidentCommentKind kind)
        => _incidents.AddComment(new IncidentComment
        {
            TenantId = incident.TenantId,
            IncidentId = incident.Id,
            Kind = kind,
            Body = body,
            AuthorUserId = _currentUser.UserId,
            IsSystemGenerated = true,
            CreatedAt = _clock.UtcNow
        });

    // --- Notifications ---

    private void NotifyOnAssignment(Incident incident, Guid? previousAssignee)
    {
        if (incident.AssignedToUserId is null || incident.AssignedToUserId == previousAssignee)
        {
            return;
        }

        _notifications.Notify(new NotificationRequest
        {
            RecipientUserId = incident.AssignedToUserId.Value,
            ActorUserId = _currentUser.UserIdOrNull,
            Kind = previousAssignee is null
                ? NotificationKind.IncidentAssigned
                : NotificationKind.IncidentReassigned,
            Severity = incident.Priority <= Priority.P2High
                ? NotificationSeverity.Warning
                : NotificationSeverity.Information,
            Title = $"{incident.Number} assigned to you",
            Body = incident.Title,
            Module = ServiceModule.Incident,
            RecordId = incident.Id,
            ActionUrl = $"/incidents/{incident.Id}",
            SendEmail = true
        });
    }

    private void NotifyStatusChanged(Incident incident, IncidentStatus previousStatus)
    {
        if (incident.Status == IncidentStatus.Resolved)
        {
            _notifications.Notify(new NotificationRequest
            {
                RecipientUserId = incident.RequesterId,
                ActorUserId = _currentUser.UserIdOrNull,
                Kind = NotificationKind.IncidentResolved,
                Severity = NotificationSeverity.Success,
                Title = $"{incident.Number} has been resolved",
                Body = incident.ResolutionNotes ?? incident.Title,
                Module = ServiceModule.Incident,
                RecordId = incident.Id,
                ActionUrl = $"/incidents/{incident.Id}",
                SendEmail = true
            });
        }
        else if (previousStatus == IncidentStatus.Resolved && incident.Status == IncidentStatus.InProgress)
        {
            var recipients = new[] { incident.AssignedToUserId, incident.RequesterId }
                .Where(g => g is not null)
                .Select(g => g!.Value);

            _notifications.NotifyMany(recipients, new NotificationRequest
            {
                ActorUserId = _currentUser.UserIdOrNull,
                Kind = NotificationKind.IncidentReopened,
                Severity = NotificationSeverity.Warning,
                Title = $"{incident.Number} has been reopened",
                Body = incident.Title,
                Module = ServiceModule.Incident,
                RecordId = incident.Id,
                ActionUrl = $"/incidents/{incident.Id}",
                SendEmail = true
            });
        }
        else if (incident.Status == IncidentStatus.Closed)
        {
            _notifications.Notify(new NotificationRequest
            {
                RecipientUserId = incident.RequesterId,
                ActorUserId = _currentUser.UserIdOrNull,
                Kind = NotificationKind.IncidentClosed,
                Title = $"{incident.Number} has been closed",
                Body = incident.Title,
                Module = ServiceModule.Incident,
                RecordId = incident.Id,
                ActionUrl = $"/incidents/{incident.Id}"
            });
        }
    }

    private void NotifyPriorityChanged(Incident incident, Priority previousPriority)
    {
        if (incident.AssignedToUserId is null)
        {
            return;
        }

        _notifications.Notify(new NotificationRequest
        {
            RecipientUserId = incident.AssignedToUserId.Value,
            ActorUserId = _currentUser.UserIdOrNull,
            Kind = NotificationKind.IncidentPriorityChanged,
            Severity = incident.Priority < previousPriority
                ? NotificationSeverity.Warning
                : NotificationSeverity.Information,
            Title = $"{incident.Number} priority changed to {incident.Priority}",
            Body = incident.Title,
            Module = ServiceModule.Incident,
            RecordId = incident.Id,
            ActionUrl = $"/incidents/{incident.Id}",
            SendEmail = incident.Priority <= Priority.P2High
        });
    }

    private void NotifyCommented(Incident incident, IncidentComment comment)
    {
        // Work notes are internal, so the requester is never told about them.
        if (comment.Kind == IncidentCommentKind.WorkNote)
        {
            if (incident.AssignedToUserId is not null)
            {
                _notifications.Notify(new NotificationRequest
                {
                    RecipientUserId = incident.AssignedToUserId.Value,
                    ActorUserId = _currentUser.UserIdOrNull,
                    Kind = NotificationKind.IncidentCommented,
                    Title = $"New work note on {incident.Number}",
                    Body = Truncate(comment.Body, 240),
                    Module = ServiceModule.Incident,
                    RecordId = incident.Id,
                    ActionUrl = $"/incidents/{incident.Id}"
                });
            }

            return;
        }

        var recipients = new List<Guid> { incident.RequesterId };
        if (incident.AssignedToUserId is not null)
        {
            recipients.Add(incident.AssignedToUserId.Value);
        }

        _notifications.NotifyMany(recipients, new NotificationRequest
        {
            ActorUserId = _currentUser.UserIdOrNull,
            Kind = NotificationKind.IncidentCommented,
            Title = $"New comment on {incident.Number}",
            Body = Truncate(comment.Body, 240),
            Module = ServiceModule.Incident,
            RecordId = incident.Id,
            ActionUrl = $"/incidents/{incident.Id}",
            SendEmail = true
        });
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
