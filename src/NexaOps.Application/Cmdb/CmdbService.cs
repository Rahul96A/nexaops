using FluentValidation;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Incidents;
using NexaOps.Application.Security;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Cmdb;
using NexaOps.Domain.Common;

namespace NexaOps.Application.Cmdb;

/// <summary>
/// The only way the configuration management database changes.
/// </summary>
public interface ICmdbService
{
    Task<PagedResult<CiSummaryDto>> SearchAsync(CiQuery query, CancellationToken ct = default);

    Task<CiDetailDto> GetAsync(Guid id, CancellationToken ct = default);

    Task<CmdbSummaryCountsDto> GetSummaryAsync(CancellationToken ct = default);

    Task<CiDetailDto> CreateAsync(UpsertCiCommand command, CancellationToken ct = default);

    Task<CiDetailDto> UpdateAsync(Guid id, UpsertCiCommand command, CancellationToken ct = default);

    Task<CiDetailDto> AddRelationshipAsync(Guid id, AddRelationshipCommand command, CancellationToken ct = default);

    Task<CiDetailDto> RemoveRelationshipAsync(
        Guid id,
        Guid targetId,
        CiRelationshipType type,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class CmdbService : ICmdbService
{
    private const string EntityType = nameof(ConfigurationItem);
    private const string NumberSequenceKey = "CI";

    private static readonly string[] SortableFields =
        ["name", "number", "type", "criticality", "status", "supportExpiresOn", "createdAt"];

    private readonly ICmdbRepository _items;
    private readonly ICmdbQueryService _queries;
    private readonly IServiceDeskReferenceRepository _reference;
    private readonly INumberSequenceService _numbers;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CmdbService> _logger;

    public CmdbService(
        ICmdbRepository items,
        ICmdbQueryService queries,
        IServiceDeskReferenceRepository reference,
        INumberSequenceService numbers,
        IAuditService audit,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ILogger<CmdbService> logger)
    {
        _items = items;
        _queries = queries;
        _reference = reference;
        _numbers = numbers;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<PagedResult<CiSummaryDto>> SearchAsync(CiQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        _currentUser.DemandPermission(Permissions.CmdbRead);

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
    public async Task<CiDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.CmdbRead);

        return await _queries.GetAsync(id, ct).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(EntityType, id);
    }

    /// <inheritdoc />
    public Task<CmdbSummaryCountsDto> GetSummaryAsync(CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.CmdbRead);
        return _queries.GetSummaryAsync(ct);
    }

    /// <inheritdoc />
    public async Task<CiDetailDto> CreateAsync(UpsertCiCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.CmdbCreate);

        await ValidateAsync(command, null, ct).ConfigureAwait(false);

        var number = await _numbers.NextAsync(NumberSequenceKey, ct).ConfigureAwait(false);

        var item = new ConfigurationItem { Number = number };
        Apply(command, item);

        _items.Add(item);

        _audit.Record(AuditAction.Create, EntityType, item.Id.ToString(), item.Name,
            $"Added {item.Name} ({item.Type}) to the CMDB.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(item.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CiDetailDto> UpdateAsync(Guid id, UpsertCiCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Retiring or disposing is a separate permission from editing: it removes an item from
        // everyone else's impact analysis, which is a bigger act than correcting a serial number.
        _currentUser.DemandPermission(
            command.Status is CiStatus.Retired or CiStatus.Disposed
                ? Permissions.CmdbRetire
                : Permissions.CmdbUpdate);

        var item = await LoadAsync(id, ct).ConfigureAwait(false);

        if (command.RowVersion is { Length: > 0 })
        {
            _items.SetExpectedVersion(item, command.RowVersion);
        }

        await ValidateAsync(command, id, ct).ConfigureAwait(false);

        var previousStatus = item.Status;
        Apply(command, item);

        _audit.Record(
            AuditAction.Update,
            EntityType,
            item.Id.ToString(),
            item.Name,
            previousStatus == item.Status
                ? "Configuration item updated."
                : $"Status changed from {previousStatus} to {item.Status}.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(item.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CiDetailDto> AddRelationshipAsync(
        Guid id,
        AddRelationshipCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A distinct permission from editing an item, so a customer can allow record
        // corrections without allowing the graph impact analysis trusts to be rewired.
        _currentUser.DemandPermission(Permissions.CmdbManageRelationships);

        var source = await LoadAsync(id, ct).ConfigureAwait(false);

        // Tenant-filtered, so an item from a neighbouring tenant reads as non-existent and the
        // graph cannot be made to span a boundary.
        _ = await _items.GetAsync(command.TargetId, ct).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(EntityType, command.TargetId);

        var relationship = new CiRelationship
        {
            SourceId = source.Id,
            TargetId = command.TargetId,
            Type = command.Type,
            Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim()
        };

        relationship.EnsureValid();

        var existing = await _items
            .GetRelationshipAsync(source.Id, command.TargetId, command.Type, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            // Idempotent: adding the same edge twice is a double-click, not an error, and a
            // duplicate would double-count in impact analysis without adding information.
            return await ReloadAsync(source.Id, ct).ConfigureAwait(false);
        }

        _items.AddRelationship(relationship);

        _audit.Record(
            AuditAction.Update,
            EntityType,
            source.Id.ToString(),
            source.Name,
            $"Added a {command.Type} relationship.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(source.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CiDetailDto> RemoveRelationshipAsync(
        Guid id,
        Guid targetId,
        CiRelationshipType type,
        CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.CmdbManageRelationships);

        var source = await LoadAsync(id, ct).ConfigureAwait(false);

        var relationship = await _items
            .GetRelationshipAsync(source.Id, targetId, type, ct)
            .ConfigureAwait(false)
            ?? throw new DomainException(
                "cmdb.relationship_not_found",
                "That relationship does not exist.");

        _items.RemoveRelationship(relationship);

        _audit.Record(
            AuditAction.Update,
            EntityType,
            source.Id.ToString(),
            source.Name,
            $"Removed a {type} relationship.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(source.Id, ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private async Task ValidateAsync(UpsertCiCommand command, Guid? exceptId, CancellationToken ct)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            failures.Add(new(nameof(command.Name), "A name is required."));
        }
        else if (await _items.NameExistsAsync(command.Name.Trim(), exceptId, ct).ConfigureAwait(false))
        {
            // The name is how people find a CI - they know "sql-prod-01", not CI0000042 - so two
            // items sharing one would make the CMDB ambiguous exactly where it is used most.
            failures.Add(new(nameof(command.Name), $"'{command.Name}' is already used by another item."));
        }

        if (command.OwnerUserId is not null
            && !await _reference.UserExistsAsync(command.OwnerUserId.Value, ct).ConfigureAwait(false))
        {
            failures.Add(new(nameof(command.OwnerUserId), "That owner does not exist."));
        }

        if (command.SupportGroupId is not null
            && !await _reference.GroupExistsAsync(command.SupportGroupId.Value, ct).ConfigureAwait(false))
        {
            failures.Add(new(nameof(command.SupportGroupId), "That support group does not exist."));
        }

        if (command.AcquiredOn is not null
            && command.SupportExpiresOn is not null
            && command.SupportExpiresOn < command.AcquiredOn)
        {
            failures.Add(new(
                nameof(command.SupportExpiresOn),
                "Support cannot expire before the item was acquired."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }

    private static void Apply(UpsertCiCommand command, ConfigurationItem item)
    {
        item.Name = command.Name.Trim();
        item.Description = command.Description?.Trim();
        item.Type = command.Type;
        item.Criticality = command.Criticality;
        item.Location = command.Location?.Trim();
        item.SerialNumber = command.SerialNumber?.Trim();
        item.Manufacturer = command.Manufacturer?.Trim();
        item.Model = command.Model?.Trim();
        item.Version = command.Version?.Trim();
        item.Environment = command.Environment?.Trim();
        item.OwnerUserId = command.OwnerUserId;
        item.SupportGroupId = command.SupportGroupId;
        item.AcquiredOn = command.AcquiredOn;
        item.SupportExpiresOn = command.SupportExpiresOn;
        item.Vendor = command.Vendor?.Trim();

        item.ChangeStatus(command.Status);
    }

    private async Task<ConfigurationItem> LoadAsync(Guid id, CancellationToken ct)
        => await _items.GetAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(EntityType, id);

    private async Task<CiDetailDto> ReloadAsync(Guid id, CancellationToken ct)
        => await _queries.GetAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(EntityType, id);
}
