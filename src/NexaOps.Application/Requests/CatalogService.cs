using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Incidents;
using NexaOps.Application.Security;
using NexaOps.Domain.Approvals;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Catalog;
using NexaOps.Domain.Common;

namespace NexaOps.Application.Requests;

/// <summary>Reads and edits the service catalogue.</summary>
public interface ICatalogService
{
    /// <summary>
    /// The catalogue as the caller may see it. Unpublished items are included only for callers
    /// who can manage the catalogue.
    /// </summary>
    Task<IReadOnlyList<CatalogItemSummaryDto>> BrowseAsync(
        string? search,
        Guid? categoryId,
        CancellationToken ct = default);

    Task<CatalogItemDetailDto> GetAsync(Guid id, CancellationToken ct = default);

    Task<CatalogItemDetailDto> CreateAsync(UpsertCatalogItemCommand command, CancellationToken ct = default);

    Task<CatalogItemDetailDto> UpdateAsync(Guid id, UpsertCatalogItemCommand command, CancellationToken ct = default);

    Task<CatalogItemDetailDto> PublishAsync(Guid id, CancellationToken ct = default);

    Task<CatalogItemDetailDto> RetireAsync(Guid id, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class CatalogService : ICatalogService
{
    private const string EntityType = nameof(CatalogItem);

    private readonly ICatalogRepository _catalog;
    private readonly IRequestQueryService _queries;
    private readonly IServiceDeskReferenceRepository _reference;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CatalogService> _logger;

    public CatalogService(
        ICatalogRepository catalog,
        IRequestQueryService queries,
        IServiceDeskReferenceRepository reference,
        IAuditService audit,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ILogger<CatalogService> logger)
    {
        _catalog = catalog;
        _queries = queries;
        _reference = reference;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CatalogItemSummaryDto>> BrowseAsync(
        string? search,
        Guid? categoryId,
        CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.CatalogRead);

        // Draft and retired items are visible only to people who maintain the catalogue. A
        // requester seeing a draft would be able to order something the business has not
        // finished defining.
        var includeUnpublished = _currentUser.HasPermission(Permissions.CatalogManage);

        return _queries.BrowseCatalogAsync(search, categoryId, includeUnpublished, ct);
    }

    /// <inheritdoc />
    public async Task<CatalogItemDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.CatalogRead);

        var item = await _queries.GetCatalogItemAsync(id, ct).ConfigureAwait(false)
                   ?? throw new EntityNotFoundException(EntityType, id);

        if (item.Status != CatalogItemStatus.Published
            && !_currentUser.HasPermission(Permissions.CatalogManage))
        {
            // Not found rather than forbidden: the existence of an unpublished item is itself
            // information about what the business is planning.
            throw new EntityNotFoundException(EntityType, id);
        }

        return item;
    }

    /// <inheritdoc />
    public async Task<CatalogItemDetailDto> CreateAsync(UpsertCatalogItemCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.CatalogManage);

        await ValidateAsync(command, null, ct).ConfigureAwait(false);

        var item = new CatalogItem { Status = CatalogItemStatus.Draft };
        Apply(command, item);

        _catalog.Add(item);
        AddVariables(command, item);

        _audit.Record(AuditAction.Create, EntityType, item.Id.ToString(), item.Name,
            $"Catalogue item '{item.Name}' created as a draft.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await ReloadAsync(item.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CatalogItemDetailDto> UpdateAsync(
        Guid id,
        UpsertCatalogItemCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.CatalogManage);

        var item = await _catalog.GetWithVariablesAsync(id, ct).ConfigureAwait(false)
                   ?? throw new EntityNotFoundException(EntityType, id);

        await ValidateAsync(command, id, ct).ConfigureAwait(false);

        if (command.RowVersion is { Length: > 0 })
        {
            _catalog.SetExpectedVersion(item, command.RowVersion);
        }

        Apply(command, item);

        // Variables are replaced wholesale rather than diffed. Ordered requests already hold
        // their own snapshot of the answers, so rewriting the definition cannot corrupt them.
        _catalog.RemoveVariables(item.Variables.ToList());
        item.Variables.Clear();
        AddVariables(command, item);

        _audit.Record(AuditAction.Update, EntityType, item.Id.ToString(), item.Name,
            $"Catalogue item '{item.Name}' updated.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await ReloadAsync(item.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CatalogItemDetailDto> PublishAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.CatalogManage);

        var item = await _catalog.GetAsync(id, ct).ConfigureAwait(false)
                   ?? throw new EntityNotFoundException(EntityType, id);

        // The domain refuses to publish something the business cannot actually deliver.
        item.Publish();

        _audit.Record(AuditAction.Update, EntityType, item.Id.ToString(), item.Name,
            $"Catalogue item '{item.Name}' published.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("Catalogue item {ItemId} published by {UserId}.", item.Id, _currentUser.UserId);

        return await ReloadAsync(item.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CatalogItemDetailDto> RetireAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.CatalogManage);

        var item = await _catalog.GetAsync(id, ct).ConfigureAwait(false)
                   ?? throw new EntityNotFoundException(EntityType, id);

        item.Retire();

        _audit.Record(AuditAction.Update, EntityType, item.Id.ToString(), item.Name,
            $"Catalogue item '{item.Name}' retired. Existing requests are unaffected.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await ReloadAsync(item.Id, ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private async Task<CatalogItemDetailDto> ReloadAsync(Guid id, CancellationToken ct)
        => await _queries.GetCatalogItemAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(EntityType, id);

    private async Task ValidateAsync(UpsertCatalogItemCommand command, Guid? exceptId, CancellationToken ct)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        if (string.IsNullOrWhiteSpace(command.Code))
        {
            failures.Add(new(nameof(command.Code), "A code is required."));
        }
        else if (await _catalog.CodeExistsAsync(command.Code.Trim(), exceptId, ct).ConfigureAwait(false))
        {
            failures.Add(new(nameof(command.Code), $"'{command.Code}' is already used by another item."));
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            failures.Add(new(nameof(command.Name), "A name is required."));
        }

        if (command.FulfilmentGroupId is not null
            && !await _reference.GroupExistsAsync(command.FulfilmentGroupId.Value, ct).ConfigureAwait(false))
        {
            failures.Add(new(nameof(command.FulfilmentGroupId), "That fulfilment group does not exist."));
        }

        if (command.ApproverUserId is not null
            && !await _reference.UserExistsAsync(command.ApproverUserId.Value, ct).ConfigureAwait(false))
        {
            failures.Add(new(nameof(command.ApproverUserId), "That approver does not exist."));
        }

        if (command.ApproverGroupId is not null
            && !await _reference.GroupExistsAsync(command.ApproverGroupId.Value, ct).ConfigureAwait(false))
        {
            failures.Add(new(nameof(command.ApproverGroupId), "That approving group does not exist."));
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < command.Variables.Count; index++)
        {
            var variable = command.Variables[index];

            if (string.IsNullOrWhiteSpace(variable.Key))
            {
                failures.Add(new($"variables[{index}].key", "A field key is required."));
                continue;
            }

            if (!keys.Add(variable.Key.Trim()))
            {
                // Duplicate keys would make one answer silently overwrite another when the
                // submitted values are serialised into a single JSON object.
                failures.Add(new($"variables[{index}].key", $"'{variable.Key}' is used more than once."));
            }

            if (string.IsNullOrWhiteSpace(variable.Label))
            {
                failures.Add(new($"variables[{index}].label", "A field label is required."));
            }

            var needsChoices = variable.Type is VariableType.Choice or VariableType.MultiChoice;

            if (needsChoices && (variable.Choices is null || variable.Choices.Count == 0))
            {
                failures.Add(new($"variables[{index}].choices",
                    "A choice field needs at least one option, or nobody can answer it."));
            }
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }

    private static void Apply(UpsertCatalogItemCommand command, CatalogItem item)
    {
        item.Code = command.Code.Trim();
        item.Name = command.Name.Trim();
        item.ShortDescription = command.ShortDescription?.Trim() ?? string.Empty;
        item.Description = command.Description?.Trim() ?? string.Empty;
        item.CategoryId = command.CategoryId;
        item.FulfilmentGroupId = command.FulfilmentGroupId;
        item.Priority = command.Priority;
        item.RequiresApproval = command.RequiresApproval;
        item.ApprovalTargetKind = command.ApprovalTargetKind;
        item.ApprovalRule = command.ApprovalRule;
        item.Cost = command.Cost;
        item.EstimatedDeliveryDays = command.EstimatedDeliveryDays;
        item.Icon = command.Icon?.Trim();
        item.SortOrder = command.SortOrder;
        item.MaxQuantity = command.MaxQuantity;

        // Only the approver relevant to the chosen kind is kept, so a stale group cannot become
        // live again by someone flipping the kind back later.
        item.ApproverUserId = command.ApprovalTargetKind == ApprovalTargetKind.User
            ? command.ApproverUserId
            : null;

        item.ApproverGroupId = command.ApprovalTargetKind == ApprovalTargetKind.Group
            ? command.ApproverGroupId
            : null;
    }

    private void AddVariables(UpsertCatalogItemCommand command, CatalogItem item)
    {
        foreach (var input in command.Variables)
        {
            var variable = new CatalogItemVariable
            {
                CatalogItemId = item.Id,
                Key = input.Key.Trim(),
                Label = input.Label.Trim(),
                HelpText = string.IsNullOrWhiteSpace(input.HelpText) ? null : input.HelpText.Trim(),
                Type = input.Type,
                IsRequired = input.IsRequired,
                SortOrder = input.SortOrder,
                DefaultValue = string.IsNullOrWhiteSpace(input.DefaultValue) ? null : input.DefaultValue.Trim(),
                ChoicesJson = input.Choices is { Count: > 0 } ? JsonSerializer.Serialize(input.Choices) : null,
                MaxLength = input.MaxLength,
                MinValue = input.MinValue,
                MaxValue = input.MaxValue
            };

            item.Variables.Add(variable);
            _catalog.AddVariable(variable);
        }
    }
}
