using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Incidents;
using NexaOps.Application.Notifications;
using NexaOps.Application.Security;
using NexaOps.Application.Sla;
using NexaOps.Application.Workflows;
using NexaOps.Domain.Approvals;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Catalog;
using NexaOps.Domain.Common;
using NexaOps.Domain.Platform;
using NexaOps.Domain.Requests;
using NexaOps.Domain.Sla;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Workflows;

namespace NexaOps.Application.Requests;

/// <summary>
/// The only way a service request changes.
/// <para>
/// Permission checks live here as well as on the endpoint, because this service is also reachable
/// from the approval flow and, later, from the workflow engine.
/// </para>
/// </summary>
public interface IRequestService
{
    Task<PagedResult<RequestSummaryDto>> SearchAsync(RequestQuery query, CancellationToken ct = default);

    Task<RequestDetailDto> GetAsync(Guid id, CancellationToken ct = default);

    Task<RequestDetailDto> GetByNumberAsync(string number, CancellationToken ct = default);

    Task<RequestSummaryCountsDto> GetSummaryAsync(CancellationToken ct = default);

    Task<RequestDetailDto> CreateAsync(CreateRequestCommand command, CancellationToken ct = default);

    Task<RequestDetailDto> UpdateAsync(Guid id, UpdateRequestCommand command, CancellationToken ct = default);

    Task<RequestDetailDto> AssignAsync(Guid id, AssignRequestCommand command, CancellationToken ct = default);

    Task<RequestDetailDto> ChangeStatusAsync(Guid id, ChangeRequestStatusCommand command, CancellationToken ct = default);

    Task<RequestDetailDto> FulfilItemAsync(Guid id, Guid itemId, FulfilRequestItemCommand command, CancellationToken ct = default);

    Task<RequestDetailDto> CancelAsync(Guid id, CancelRequestCommand command, CancellationToken ct = default);

    Task<IReadOnlyList<RequestCommentDto>> GetCommentsAsync(Guid id, CancellationToken ct = default);

    Task<RequestCommentDto> AddCommentAsync(Guid id, AddRequestCommentCommand command, CancellationToken ct = default);

    Task<IReadOnlyList<ApprovalDto>> GetMyApprovalsAsync(bool outstandingOnly, CancellationToken ct = default);

    Task<RequestDetailDto> DecideApprovalAsync(Guid approvalId, DecideApprovalCommand command, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class RequestService : IRequestService
{
    private const string EntityType = nameof(ServiceRequest);
    private const string ApprovalModule = "Request";
    private const string NumberSequenceKey = "REQ";

    private static readonly string[] SortableFields =
        ["createdAt", "number", "priority", "status", "nextSlaDueAt", "totalCost"];

    private readonly IRequestRepository _requests;
    private readonly ICatalogRepository _catalog;
    private readonly IRequestQueryService _queries;
    private readonly IApprovalService _approvals;
    private readonly IServiceDeskReferenceRepository _reference;
    private readonly INumberSequenceService _numbers;
    private readonly ISlaService _sla;
    private readonly INotificationService _notifications;
    private readonly IWorkflowEngine _workflows;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<RequestService> _logger;

    public RequestService(
        IRequestRepository requests,
        ICatalogRepository catalog,
        IRequestQueryService queries,
        IApprovalService approvals,
        IServiceDeskReferenceRepository reference,
        INumberSequenceService numbers,
        ISlaService sla,
        INotificationService notifications,
        IWorkflowEngine workflows,
        IAuditService audit,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<RequestService> logger)
    {
        _requests = requests;
        _catalog = catalog;
        _queries = queries;
        _approvals = approvals;
        _reference = reference;
        _numbers = numbers;
        _sla = sla;
        _notifications = notifications;
        _workflows = workflows;
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
    public Task<PagedResult<RequestSummaryDto>> SearchAsync(RequestQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        _currentUser.DemandPermission(Permissions.RequestRead);

        if (!SortableFields.Contains(query.SortBy, StringComparer.OrdinalIgnoreCase))
        {
            // A 400 with a field error, not a 409 and not silent interpolation into SQL.
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
    public async Task<RequestDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.RequestRead);

        return await _queries.GetAsync(id, ct).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(EntityType, id);
    }

    /// <inheritdoc />
    public async Task<RequestDetailDto> GetByNumberAsync(string number, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.RequestRead);

        return await _queries.GetByNumberAsync(number, ct).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(EntityType, number);
    }

    /// <inheritdoc />
    public Task<RequestSummaryCountsDto> GetSummaryAsync(CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.RequestRead);
        return _queries.GetSummaryAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RequestCommentDto>> GetCommentsAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.RequestRead);

        return await _queries.GetCommentsAsync(id, ct).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(EntityType, id);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ApprovalDto>> GetMyApprovalsAsync(bool outstandingOnly, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.ApprovalAct);
        return _queries.GetMyApprovalsAsync(outstandingOnly, ct);
    }

    // -----------------------------------------------------------------
    // Create
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<RequestDetailDto> CreateAsync(CreateRequestCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.RequestCreate);

        if (command.Items.Count == 0)
        {
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(command.Items), "Order at least one catalogue item.")
            ]);
        }

        var requestedForId = command.RequestedForId ?? _currentUser.UserId;

        if (requestedForId != _currentUser.UserId
            && !await _reference.UserExistsAsync(requestedForId, ct).ConfigureAwait(false))
        {
            // Reads as a non-existent user because the look-up is tenant-filtered, so a foreign
            // directory cannot be probed through this field.
            throw new EntityNotFoundException("User", requestedForId);
        }

        var catalogIds = command.Items.Select(i => i.CatalogItemId).Distinct().ToList();
        var catalogItems = await _catalog.GetManyWithVariablesAsync(catalogIds, ct).ConfigureAwait(false);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var failures = new List<FluentValidation.Results.ValidationFailure>();

        for (var index = 0; index < command.Items.Count; index++)
        {
            var line = command.Items[index];

            if (!byId.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new EntityNotFoundException(nameof(CatalogItem), line.CatalogItemId);
            }

            if (!catalogItem.IsOrderable)
            {
                // A draft or retired item is not orderable. Reported as a validation failure
                // rather than a 404, because the caller legitimately knows the item exists.
                failures.Add(new($"items[{index}].catalogItemId",
                    $"{catalogItem.Name} is not currently available to order."));
                continue;
            }

            if (line.Quantity < 1)
            {
                failures.Add(new($"items[{index}].quantity", "Quantity must be at least one."));
            }

            if (catalogItem.MaxQuantity is > 0 && line.Quantity > catalogItem.MaxQuantity)
            {
                failures.Add(new($"items[{index}].quantity",
                    $"You can order at most {catalogItem.MaxQuantity} of {catalogItem.Name}."));
            }

            failures.AddRange(ValidateAnswers(catalogItem, line, index));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var now = _clock.UtcNow;
        var (organizationId, departmentId) =
            await _reference.GetUserPlacementAsync(requestedForId, ct).ConfigureAwait(false);

        var number = await _numbers.NextAsync(NumberSequenceKey, ct).ConfigureAwait(false);

        var firstItem = byId[command.Items[0].CatalogItemId];

        var request = new ServiceRequest
        {
            Number = number,
            Title = string.IsNullOrWhiteSpace(command.Title)
                ? BuildTitle(command, byId)
                : command.Title.Trim(),
            Description = command.Description?.Trim() ?? string.Empty,
            RequesterId = _currentUser.UserId,
            RequestedForId = requestedForId,
            OrganizationId = organizationId,
            DepartmentId = departmentId,
            CategoryId = firstItem.CategoryId,

            // The highest priority across the ordered lines governs the request as a whole,
            // so a single urgent item is not diluted by routine ones ordered alongside it.
            Priority = command.Items.Min(i => byId[i.CatalogItemId].Priority),

            FulfilmentGroupId = firstItem.FulfilmentGroupId,
            Channel = command.Channel,
            RequiredByDate = command.RequiredByDate,
            Status = RequestStatus.Draft
        };

        _requests.Add(request);

        decimal? total = null;

        foreach (var line in command.Items)
        {
            var catalogItem = byId[line.CatalogItemId];

            var item = new RequestItem
            {
                ServiceRequestId = request.Id,
                CatalogItemId = catalogItem.Id,

                // Snapshot. Editing the catalogue later must not rewrite what was ordered, nor
                // what an approver saw when they authorised it.
                CatalogItemName = catalogItem.Name,
                UnitCost = catalogItem.Cost,

                Quantity = line.Quantity,
                Status = RequestItemStatus.Pending,
                VariableValuesJson = JsonSerializer.Serialize(NormaliseAnswers(catalogItem, line)),
                FulfilmentGroupId = catalogItem.FulfilmentGroupId
            };

            request.Items.Add(item);
            _requests.AddItem(item);

            if (item.LineCost is not null)
            {
                total = (total ?? 0m) + item.LineCost.Value;
            }
        }

        request.TotalCost = total;

        var requirements = await BuildApprovalRequirementsAsync(
            command.Items.Select(i => byId[i.CatalogItemId]).ToList(),
            requestedForId,
            ct).ConfigureAwait(false);

        var approvalRaised = requirements.Count > 0
            && await _approvals
                .RaiseAsync(ApprovalModule, request.Id, $"{request.Number} — {request.Title}", requirements, ct)
                .ConfigureAwait(false);

        request.Submit(approvalRaised, _currentUser.UserId, now);

        // Clocks attach on submission and immediately reflect whether the request is waiting on
        // an approver: RequestStateMachine.PausesSla says AwaitingApproval pauses, so the
        // fulfilment commitment does not run against a desk that has not been authorised to act.
        await _sla.AttachClocksAsync(request, ct).ConfigureAwait(false);

        if (RequestStateMachine.PausesSla(request.Status))
        {
            await _sla.OnStatusChangedAsync(
                    request,
                    new SlaStatusChange(WasPaused: false, IsPaused: true),
                    ct)
                .ConfigureAwait(false);
        }

        _audit.Record(
            AuditAction.Create,
            EntityType,
            request.Id.ToString(),
            request.Number,
            $"Raised {request.Number} with {request.Items.Count} item(s).");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Service request {Number} raised by {UserId} with {ItemCount} item(s); approval required: {Approval}.",
            request.Number, _currentUser.UserId, request.Items.Count, approvalRaised);

        // After the request is committed, and unable to fail it. See the engine.
        await _workflows.RunAsync(request, WorkflowTrigger.RecordCreated, ct).ConfigureAwait(false);

        return await ReloadAsync(request.Id, ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Updates
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public async Task<RequestDetailDto> UpdateAsync(Guid id, UpdateRequestCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.RequestUpdate);

        // Loaded with clocks, because a priority change re-targets live commitments.
        var request = await _requests.GetWithClocksAsync(id, ct).ConfigureAwait(false)
                      ?? throw new EntityNotFoundException(EntityType, id);

        ApplyConcurrencyToken(request, command.RowVersion);

        if (!string.IsNullOrWhiteSpace(command.Title))
        {
            request.Title = command.Title.Trim();
        }

        if (command.Description is not null)
        {
            request.Description = command.Description.Trim();
        }

        if (command.CategoryId is not null)
        {
            _ = await _reference.GetCategoryAsync(command.CategoryId.Value, ct).ConfigureAwait(false)
                ?? throw new EntityNotFoundException("Category", command.CategoryId.Value);

            request.CategoryId = command.CategoryId;
        }

        if (command.Priority is not null && command.Priority != request.Priority)
        {
            request.Priority = command.Priority.Value;

            await _sla.OnPriorityChangedAsync(request, ct).ConfigureAwait(false);
        }

        if (command.RequiredByDate is not null)
        {
            request.RequiredByDate = command.RequiredByDate;
        }

        _audit.Record(AuditAction.Update, EntityType, request.Id.ToString(), request.Number, "Request updated.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(request.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RequestDetailDto> AssignAsync(Guid id, AssignRequestCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.RequestAssign);

        var request = await LoadAsync(id, ct).ConfigureAwait(false);
        ApplyConcurrencyToken(request, command.RowVersion);

        if (command.FulfilmentGroupId is not null
            && !await _reference.GroupExistsAsync(command.FulfilmentGroupId.Value, ct).ConfigureAwait(false))
        {
            throw new EntityNotFoundException("Group", command.FulfilmentGroupId.Value);
        }

        if (command.AssignedToUserId is not null
            && !await _reference.UserExistsAsync(command.AssignedToUserId.Value, ct).ConfigureAwait(false))
        {
            throw new EntityNotFoundException("User", command.AssignedToUserId.Value);
        }

        var previousAssignee = request.AssignedToUserId;
        request.Assign(command.FulfilmentGroupId, command.AssignedToUserId);

        _audit.Record(AuditAction.Assign, EntityType, request.Id.ToString(), request.Number, "Assignment changed.");

        if (request.AssignedToUserId is not null && request.AssignedToUserId != previousAssignee)
        {
            _notifications.Notify(new NotificationRequest
            {
                RecipientUserId = request.AssignedToUserId.Value,
                ActorUserId = _currentUser.UserIdOrNull,
                Kind = NotificationKind.RequestAssigned,
                Severity = NotificationSeverity.Information,
                Title = $"{request.Number} assigned to you",
                Body = request.Title,
                Module = ServiceModule.Request,
                RecordId = request.Id,
                ActionUrl = $"/requests/{request.Id}",
                SendEmail = true
            });
        }

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        await _workflows.RunAsync(request, WorkflowTrigger.AssignmentChanged, ct).ConfigureAwait(false);

        return await ReloadAsync(request.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RequestDetailDto> ChangeStatusAsync(
        Guid id,
        ChangeRequestStatusCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Fulfilment and closure are distinct permissions, exactly as resolve and close are on
        // the incident side.
        _currentUser.DemandPermission(command.Status switch
        {
            RequestStatus.Fulfilled => Permissions.RequestFulfil,
            RequestStatus.Closed => Permissions.RequestClose,
            RequestStatus.Cancelled => Permissions.RequestCancel,
            _ => Permissions.RequestUpdate
        });

        // Loaded with its lines, because completing the request asks whether every line has
        // settled. Loading without them leaves the collection empty, and AllItemsSettled reports
        // false for an empty request - so a fully delivered request could never be completed.
        var request = await _requests.GetWithLinesAsync(id, ct).ConfigureAwait(false)
                      ?? throw new EntityNotFoundException(EntityType, id);

        ApplyConcurrencyToken(request, command.RowVersion);

        var now = _clock.UtcNow;
        var previous = request.Status;

        if (command.Status == RequestStatus.Pending)
        {
            request.PutOnHold(
                command.PendingReason ?? RequestPendingReason.AwaitingRequester,
                _currentUser.UserId,
                now);
        }
        else
        {
            if (command.Status == RequestStatus.Fulfilled && !request.AllItemsSettled())
            {
                throw new DomainException(
                    "request.items_outstanding",
                    "Every item must be delivered or cancelled before the request can be completed.");
            }

            request.TransitionTo(command.Status, _currentUser.UserId, now);
        }

        // The request lifecycle is translated into the engine's vocabulary, so one set of SLA
        // rules serves both modules while each keeps its own statuses.
        await _sla.OnStatusChangedAsync(request, RequestSlaMapping.For(request, previous), ct)
            .ConfigureAwait(false);

        _audit.Record(
            AuditAction.Update,
            EntityType,
            request.Id.ToString(),
            request.Number,
            $"Status changed from {previous} to {request.Status}." +
            (string.IsNullOrWhiteSpace(command.Note) ? string.Empty : $" {command.Note.Trim()}"));

        if (request.Status == RequestStatus.Fulfilled)
        {
            NotifyRequester(request, NotificationKind.RequestFulfilled, NotificationSeverity.Success,
                $"{request.Number} has been delivered", request.Title);
        }

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        await _workflows.RunAsync(request, WorkflowTrigger.StatusChanged, ct).ConfigureAwait(false);

        return await ReloadAsync(request.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RequestDetailDto> FulfilItemAsync(
        Guid id,
        Guid itemId,
        FulfilRequestItemCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.RequestFulfil);

        var request = await _requests.GetWithLinesAsync(id, ct).ConfigureAwait(false)
                      ?? throw new EntityNotFoundException(EntityType, id);

        var item = request.Items.FirstOrDefault(i => i.Id == itemId)
                   ?? throw new EntityNotFoundException(nameof(RequestItem), itemId);

        if (request.Status is RequestStatus.Draft or RequestStatus.AwaitingApproval)
        {
            throw new DomainException(
                "request.not_authorised",
                "This request has not been authorised yet, so its items cannot be delivered.");
        }

        var now = _clock.UtcNow;
        item.Fulfil(_currentUser.UserId, now, command.Notes);

        // Delivering the first line moves the request into active fulfilment, so an agent does
        // not have to remember to set the status separately.
        if (request.Status == RequestStatus.Approved)
        {
            request.TransitionTo(RequestStatus.InProgress, _currentUser.UserId, now);
        }

        _audit.Record(
            AuditAction.Update,
            EntityType,
            request.Id.ToString(),
            request.Number,
            $"Item '{item.CatalogItemName}' delivered.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(request.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RequestDetailDto> CancelAsync(Guid id, CancelRequestCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = await _requests.GetWithLinesAsync(id, ct).ConfigureAwait(false)
                      ?? throw new EntityNotFoundException(EntityType, id);

        // A requester may withdraw their own request without holding request.cancel, which is a
        // management permission for cancelling somebody else's.
        if (request.RequesterId != _currentUser.UserId)
        {
            _currentUser.DemandPermission(Permissions.RequestCancel);
        }

        ApplyConcurrencyToken(request, command.RowVersion);

        var now = _clock.UtcNow;
        var previous = request.Status;
        request.Cancel(command.Reason, _currentUser.UserId, now);

        // Abandoned, not breached: nobody failed a commitment on work that was called off.
        await _sla.OnStatusChangedAsync(request, RequestSlaMapping.For(request, previous), ct)
            .ConfigureAwait(false);

        await _approvals.CancelOutstandingAsync(ApprovalModule, request.Id, ct).ConfigureAwait(false);


        _audit.Record(
            AuditAction.Update,
            EntityType,
            request.Id.ToString(),
            request.Number,
            $"Request cancelled. {request.CancellationReason}");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(request.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RequestCommentDto> AddCommentAsync(
        Guid id,
        AddRequestCommentCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        _currentUser.DemandPermission(command.Kind == IncidentCommentKind.WorkNote
            ? Permissions.RequestWorkNoteCreate
            : Permissions.RequestCommentCreate);

        if (string.IsNullOrWhiteSpace(command.Body))
        {
            throw new ValidationException(
            [
                new FluentValidation.Results.ValidationFailure(nameof(command.Body), "A comment cannot be empty.")
            ]);
        }

        var request = await LoadAsync(id, ct).ConfigureAwait(false);

        var comment = new RequestComment
        {
            ServiceRequestId = request.Id,
            Kind = command.Kind,
            Body = command.Body.Trim(),
            AuthorId = _currentUser.UserId,
            AuthorDisplayName = _currentUser.DisplayName
        };

        _requests.AddComment(comment);

        _audit.Record(
            AuditAction.Update,
            EntityType,
            request.Id.ToString(),
            request.Number,
            command.Kind == IncidentCommentKind.WorkNote ? "Work note added." : "Comment added.");

        if (command.Kind == IncidentCommentKind.PublicComment)
        {
            NotifyRequester(request, NotificationKind.RequestCommented, NotificationSeverity.Information,
                $"New comment on {request.Number}", comment.Body);
        }

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return new RequestCommentDto(
            comment.Id, comment.Kind, comment.Body, comment.AuthorId,
            comment.AuthorDisplayName, comment.CreatedAt);
    }

    /// <inheritdoc />
    public async Task<RequestDetailDto> DecideApprovalAsync(
        Guid approvalId,
        DecideApprovalCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var decision = await _approvals.DecideAsync(approvalId, command, ct).ConfigureAwait(false);

        if (!string.Equals(decision.Module, ApprovalModule, StringComparison.Ordinal))
        {
            // The approval belongs to a module this service does not own. Approvals are shared
            // infrastructure, so this is a real possibility once change management lands.
            throw new EntityNotFoundException(EntityType, decision.RecordId);
        }

        var decided = await _requests.GetWithLinesAsync(decision.RecordId, ct).ConfigureAwait(false)
                      ?? throw new EntityNotFoundException(EntityType, decision.RecordId);

        var now = _clock.UtcNow;

        switch (decision.Outcome)
        {
            case ApprovalRecordOutcome.Approved:
                decided.RecordApproved(_currentUser.UserId, now);

                // The fulfilment clock starts only now: the desk could not act before.
                await _sla.OnStatusChangedAsync(
                        decided,
                        RequestSlaMapping.For(decided, RequestStatus.AwaitingApproval),
                        ct)
                    .ConfigureAwait(false);

                // The fulfilment clock starts only now, because the desk could not act before.

                NotifyRequester(decided, NotificationKind.ApprovalDecided, NotificationSeverity.Success,
                    $"{decided.Number} approved", "Your request has been approved and is now with the service desk.");
                break;

            case ApprovalRecordOutcome.Rejected:
                decided.RecordRejected(command.Comment ?? "No reason given.", _currentUser.UserId, now);

                await _sla.OnStatusChangedAsync(
                        decided,
                        RequestSlaMapping.For(decided, RequestStatus.AwaitingApproval),
                        ct)
                    .ConfigureAwait(false);

                await _approvals.CancelOutstandingAsync(ApprovalModule, decided.Id, ct).ConfigureAwait(false);

                NotifyRequester(decided, NotificationKind.RequestRejected, NotificationSeverity.Warning,
                    $"{decided.Number} was not approved", decided.RejectionReason ?? string.Empty);
                break;
        }

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(decided.Id, ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private async Task<ServiceRequest> LoadAsync(Guid id, CancellationToken ct)
        => await _requests.GetAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(EntityType, id);

    private async Task<RequestDetailDto> ReloadAsync(Guid id, CancellationToken ct)
        => await _queries.GetAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(EntityType, id);

    private void ApplyConcurrencyToken(ServiceRequest request, byte[]? rowVersion)
    {
        if (rowVersion is { Length: > 0 })
        {
            _requests.SetExpectedVersion(request, rowVersion);
        }
    }

    private void NotifyRequester(
        ServiceRequest request,
        NotificationKind kind,
        NotificationSeverity severity,
        string title,
        string body)
    {
        foreach (var recipient in new[] { request.RequesterId, request.RequestedForId }.Distinct())
        {
            _notifications.Notify(new NotificationRequest
            {
                RecipientUserId = recipient,
                ActorUserId = _currentUser.UserIdOrNull,
                Kind = kind,
                Severity = severity,
                Title = title,
                Body = body,
                Module = ServiceModule.Request,
                RecordId = request.Id,
                ActionUrl = $"/requests/{request.Id}",
                SendEmail = true
            });
        }
    }

    /// <summary>
    /// Turns the ordered items' approval configuration into concrete requirements, resolving a
    /// manager target to an actual person at this moment rather than storing a rule that could
    /// go stale between ordering and deciding.
    /// </summary>
    private async Task<IReadOnlyList<ApprovalRequirement>> BuildApprovalRequirementsAsync(
        IReadOnlyList<CatalogItem> items,
        Guid requestedForId,
        CancellationToken ct)
    {
        var requirements = new List<ApprovalRequirement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items.Where(i => i.RequiresApproval))
        {
            Guid? approverUserId = item.ApproverUserId;
            Guid? approverGroupId = item.ApproverGroupId;

            if (item.ApprovalTargetKind == ApprovalTargetKind.Manager)
            {
                approverUserId = await _reference.GetManagerIdAsync(requestedForId, ct).ConfigureAwait(false);
                approverGroupId = null;
            }

            // Two items needing the same approver produce one approval, not two. Asking one
            // person to click approve twice for a single order is a defect, not thoroughness.
            var key = $"{item.ApprovalTargetKind}:{approverUserId}:{approverGroupId}";
            if (!seen.Add(key))
            {
                continue;
            }

            requirements.Add(new ApprovalRequirement(
                item.ApprovalTargetKind, approverUserId, approverGroupId, item.ApprovalRule));
        }

        return requirements;
    }

    private static string BuildTitle(CreateRequestCommand command, IReadOnlyDictionary<Guid, CatalogItem> byId)
    {
        var first = byId[command.Items[0].CatalogItemId].Name;

        return command.Items.Count == 1
            ? first
            : $"{first} and {command.Items.Count - 1} more item(s)";
    }

    private static IEnumerable<FluentValidation.Results.ValidationFailure> ValidateAnswers(
        CatalogItem catalogItem,
        RequestLineInput line,
        int index)
    {
        foreach (var variable in catalogItem.Variables)
        {
            line.Values.TryGetValue(variable.Key, out var submitted);

            var error = variable.Validate(submitted);
            if (error is not null)
            {
                yield return new($"items[{index}].values.{variable.Key}", error);
            }
        }

        // An answer to a field this item does not define is refused rather than silently stored,
        // so a crafted payload cannot smuggle data onto a record.
        var known = catalogItem.Variables.Select(v => v.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var key in line.Values.Keys.Where(k => !known.Contains(k)))
        {
            yield return new($"items[{index}].values.{key}", $"'{key}' is not a field on this item.");
        }
    }

    private static Dictionary<string, string> NormaliseAnswers(CatalogItem catalogItem, RequestLineInput line)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var variable in catalogItem.Variables.OrderBy(v => v.SortOrder))
        {
            if (line.Values.TryGetValue(variable.Key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                result[variable.Key] = value.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(variable.DefaultValue))
            {
                result[variable.Key] = variable.DefaultValue;
            }
        }

        return result;
    }
}
