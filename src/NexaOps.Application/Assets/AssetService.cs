using FluentValidation;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Incidents;
using NexaOps.Application.Security;
using NexaOps.Domain.Assets;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Common;

namespace NexaOps.Application.Assets;

/// <summary>
/// The only way the asset register and licence position change.
/// </summary>
public interface IAssetService
{
    Task<PagedResult<AssetSummaryDto>> SearchAsync(AssetQuery query, CancellationToken ct = default);

    Task<AssetDetailDto> GetAsync(Guid id, CancellationToken ct = default);

    Task<AssetSummaryCountsDto> GetSummaryAsync(CancellationToken ct = default);

    Task<AssetDetailDto> CreateAsync(UpsertAssetCommand command, CancellationToken ct = default);

    Task<AssetDetailDto> UpdateAsync(Guid id, UpsertAssetCommand command, CancellationToken ct = default);

    Task<AssetDetailDto> AssignAsync(Guid id, AssignAssetCommand command, CancellationToken ct = default);

    Task<AssetDetailDto> ReturnAsync(Guid id, ReturnAssetCommand command, CancellationToken ct = default);

    Task<AssetDetailDto> DisposeAsync(Guid id, DisposeAssetCommand command, CancellationToken ct = default);

    Task<IReadOnlyList<LicenceSummaryDto>> GetLicencesAsync(CancellationToken ct = default);

    Task<LicenceSummaryDto> CreateLicenceAsync(UpsertLicenceCommand command, CancellationToken ct = default);

    Task<LicenceSummaryDto> UpdateLicenceAsync(Guid id, UpsertLicenceCommand command, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class AssetService : IAssetService
{
    private const string EntityType = nameof(Asset);
    private const string LicenceEntityType = nameof(SoftwareLicence);
    private const string AssetSequenceKey = "ASSET";
    private const string LicenceSequenceKey = "LIC";

    private static readonly string[] SortableFields =
        ["assetTag", "name", "number", "status", "kind", "purchasedOn", "warrantyExpiresOn", "createdAt"];

    private readonly IAssetRepository _assets;
    private readonly IAssetQueryService _queries;
    private readonly IServiceDeskReferenceRepository _reference;
    private readonly INumberSequenceService _numbers;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AssetService> _logger;

    public AssetService(
        IAssetRepository assets,
        IAssetQueryService queries,
        IServiceDeskReferenceRepository reference,
        INumberSequenceService numbers,
        IAuditService audit,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<AssetService> logger)
    {
        _assets = assets;
        _queries = queries;
        _reference = reference;
        _numbers = numbers;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<PagedResult<AssetSummaryDto>> SearchAsync(AssetQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        _currentUser.DemandPermission(Permissions.AssetRead);

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
    public async Task<AssetDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.AssetRead);

        return await _queries.GetAsync(id, ct).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(EntityType, id);
    }

    /// <inheritdoc />
    public Task<AssetSummaryCountsDto> GetSummaryAsync(CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.AssetRead);
        return _queries.GetSummaryAsync(ct);
    }

    /// <inheritdoc />
    public async Task<AssetDetailDto> CreateAsync(UpsertAssetCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.AssetCreate);

        await ValidateAsync(command, null, ct).ConfigureAwait(false);

        var number = await _numbers.NextAsync(AssetSequenceKey, ct).ConfigureAwait(false);

        var asset = new Asset { Number = number };
        Apply(command, asset);

        _assets.Add(asset);

        _audit.Record(AuditAction.Create, EntityType, asset.Id.ToString(), asset.AssetTag,
            $"Added {asset.AssetTag} ({asset.Name}) to the asset register.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(asset.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AssetDetailDto> UpdateAsync(Guid id, UpsertAssetCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.AssetUpdate);

        var asset = await LoadAsync(id, ct).ConfigureAwait(false);

        if (command.RowVersion is { Length: > 0 })
        {
            _assets.SetExpectedVersion(asset, command.RowVersion);
        }

        await ValidateAsync(command, id, ct).ConfigureAwait(false);

        // Disposal has its own endpoint because it closes custody and stamps a date. Reaching it
        // through a general edit would skip both.
        if (command.Status == AssetStatus.Disposed && asset.Status != AssetStatus.Disposed)
        {
            throw new DomainException(
                "asset.use_disposal_endpoint",
                "Dispose of an asset through the disposal action, so custody is closed and the date recorded.");
        }

        Apply(command, asset);

        _audit.Record(AuditAction.Update, EntityType, asset.Id.ToString(), asset.AssetTag, "Asset updated.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(asset.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AssetDetailDto> AssignAsync(Guid id, AssignAssetCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.AssetAssign);

        var asset = await LoadWithHistoryAsync(id, ct).ConfigureAwait(false);

        // Tenant-filtered, so an asset cannot be issued to somebody in a neighbouring directory.
        if (!await _reference.UserExistsAsync(command.UserId, ct).ConfigureAwait(false))
        {
            throw new EntityNotFoundException("User", command.UserId);
        }

        var assignment = asset.AssignTo(command.UserId, _clock.UtcNow, command.Note);
        _assets.AddAssignment(assignment);

        _audit.Record(
            AuditAction.Assign,
            EntityType,
            asset.Id.ToString(),
            asset.AssetTag,
            $"Issued {asset.AssetTag} to a user.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(asset.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AssetDetailDto> ReturnAsync(Guid id, ReturnAssetCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.AssetAssign);

        var asset = await LoadWithHistoryAsync(id, ct).ConfigureAwait(false);

        // A handback lands the asset back in the register, in stock, in repair, or written off as
        // unaccounted for. It may not land on Retired or Disposed: those end the asset's working
        // life, are held behind a narrower permission, and stamp a date that a return does not.
        // Without this the return endpoint would be a way around asset.dispose, and would leave a
        // disposed asset with no disposal date for finance to explain.
        if (command.ReturnTo is not (AssetStatus.InStock or AssetStatus.InRepair or AssetStatus.Lost))
        {
            throw new DomainException(
                "asset.invalid_return_state",
                "An asset can be returned to stock, sent for repair, or recorded as unaccounted for. "
                + "Retiring or disposing of it is a separate action.");
        }

        asset.Return(_clock.UtcNow, command.Note, command.ReturnTo);

        _audit.Record(
            AuditAction.Update,
            EntityType,
            asset.Id.ToString(),
            asset.AssetTag,
            $"{asset.AssetTag} returned to {command.ReturnTo}.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ReloadAsync(asset.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AssetDetailDto> DisposeAsync(Guid id, DisposeAssetCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Narrowly held: disposal removes something a finance audit expects to be able to count.
        _currentUser.DemandPermission(Permissions.AssetDispose);

        var asset = await LoadWithHistoryAsync(id, ct).ConfigureAwait(false);

        asset.Dispose(command.DisposedOn, command.Notes, _clock.UtcNow);

        _audit.Record(
            AuditAction.Update,
            EntityType,
            asset.Id.ToString(),
            asset.AssetTag,
            $"{asset.AssetTag} disposed of on {command.DisposedOn:yyyy-MM-dd}. {asset.DisposalNotes}");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Asset {AssetTag} disposed of by {UserId}.", asset.AssetTag, _currentUser.UserId);

        return await ReloadAsync(asset.Id, ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Licences
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public Task<IReadOnlyList<LicenceSummaryDto>> GetLicencesAsync(CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.LicenceRead);
        return _queries.GetLicencesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<LicenceSummaryDto> CreateLicenceAsync(
        UpsertLicenceCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.LicenceManage);

        ValidateLicence(command);

        var number = await _numbers.NextAsync(LicenceSequenceKey, ct).ConfigureAwait(false);

        var licence = new SoftwareLicence { Number = number };
        ApplyLicence(command, licence);

        _assets.AddLicence(licence);

        _audit.Record(AuditAction.Create, LicenceEntityType, licence.Id.ToString(), licence.ProductName,
            $"Recorded a licence for {licence.ProductName}: {licence.EntitlementCount} entitlement(s).");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await _queries.GetLicenceAsync(licence.Id, ct).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(LicenceEntityType, licence.Id);
    }

    /// <inheritdoc />
    public async Task<LicenceSummaryDto> UpdateLicenceAsync(
        Guid id,
        UpsertLicenceCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.LicenceManage);

        var licence = await _assets.GetLicenceAsync(id, ct).ConfigureAwait(false)
                      ?? throw new EntityNotFoundException(LicenceEntityType, id);

        if (command.RowVersion is { Length: > 0 })
        {
            _assets.SetLicenceExpectedVersion(licence, command.RowVersion);
        }

        ValidateLicence(command);

        var previousCompliance = licence.ComplianceAt(Today());
        ApplyLicence(command, licence);
        var compliance = licence.ComplianceAt(Today());

        _audit.Record(AuditAction.Update, LicenceEntityType, licence.Id.ToString(), licence.ProductName,
            previousCompliance == compliance
                ? "Licence updated."
                : $"Compliance changed from {previousCompliance} to {compliance}.");

        if (compliance == ComplianceState.OverDeployed)
        {
            _logger.LogWarning(
                "Licence {Product} is over-deployed by {Seats} seat(s).",
                licence.ProductName, licence.OverDeployedBy);
        }

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await _queries.GetLicenceAsync(licence.Id, ct).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(LicenceEntityType, licence.Id);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private DateOnly Today() => DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

    private async Task ValidateAsync(UpsertAssetCommand command, Guid? exceptId, CancellationToken ct)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        if (string.IsNullOrWhiteSpace(command.AssetTag))
        {
            failures.Add(new(nameof(command.AssetTag), "An asset tag is required."));
        }
        else if (await _assets.TagExistsAsync(command.AssetTag.Trim(), exceptId, ct).ConfigureAwait(false))
        {
            // The tag is what a finance audit physically counts, so a duplicate makes the
            // register unreconcilable against the floor.
            failures.Add(new(nameof(command.AssetTag), $"'{command.AssetTag}' is already used by another asset."));
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            failures.Add(new(nameof(command.Name), "A name is required."));
        }

        if (command.UsefulLifeMonths is < 0)
        {
            failures.Add(new(nameof(command.UsefulLifeMonths), "Useful life cannot be negative."));
        }

        if (command.PurchaseCost is < 0)
        {
            failures.Add(new(nameof(command.PurchaseCost), "Purchase cost cannot be negative."));
        }

        if (command.PurchasedOn is not null
            && command.WarrantyExpiresOn is not null
            && command.WarrantyExpiresOn < command.PurchasedOn)
        {
            failures.Add(new(
                nameof(command.WarrantyExpiresOn),
                "Warranty cannot expire before the asset was purchased."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }

    private static void ValidateLicence(UpsertLicenceCommand command)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        if (string.IsNullOrWhiteSpace(command.ProductName))
        {
            failures.Add(new(nameof(command.ProductName), "A product name is required."));
        }

        if (command.EntitlementCount < 0)
        {
            failures.Add(new(nameof(command.EntitlementCount), "Entitlements cannot be negative."));
        }

        if (command.DeployedCount < 0)
        {
            failures.Add(new(nameof(command.DeployedCount), "Deployments cannot be negative."));
        }

        if (command.StartsOn is not null && command.ExpiresOn is not null && command.ExpiresOn < command.StartsOn)
        {
            failures.Add(new(nameof(command.ExpiresOn), "A licence cannot expire before it starts."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }

    private static void Apply(UpsertAssetCommand command, Asset asset)
    {
        asset.AssetTag = command.AssetTag.Trim();
        asset.Name = command.Name.Trim();
        asset.Kind = command.Kind;
        asset.Manufacturer = command.Manufacturer?.Trim();
        asset.Model = command.Model?.Trim();
        asset.SerialNumber = command.SerialNumber?.Trim();
        asset.Location = command.Location?.Trim();
        asset.PurchasedOn = command.PurchasedOn;
        asset.PurchaseCost = command.PurchaseCost;
        asset.Vendor = command.Vendor?.Trim();
        asset.PurchaseOrderNumber = command.PurchaseOrderNumber?.Trim();
        asset.WarrantyExpiresOn = command.WarrantyExpiresOn;
        asset.UsefulLifeMonths = command.UsefulLifeMonths;
        asset.ConfigurationItemId = command.ConfigurationItemId;

        // Custody drives Assigned; a general edit must not silently orphan a holder.
        if (asset.AssignedToUserId is null && command.Status != AssetStatus.Assigned)
        {
            asset.Status = command.Status;
        }
    }

    private static void ApplyLicence(UpsertLicenceCommand command, SoftwareLicence licence)
    {
        licence.ProductName = command.ProductName.Trim();
        licence.Publisher = command.Publisher?.Trim();
        licence.Version = command.Version?.Trim();
        licence.AgreementReference = command.AgreementReference?.Trim();
        licence.Model = command.Model;
        licence.StartsOn = command.StartsOn;
        licence.ExpiresOn = command.ExpiresOn;
        licence.AnnualCost = command.AnnualCost;
        licence.Vendor = command.Vendor?.Trim();
        licence.Notes = command.Notes?.Trim();

        licence.SetEntitlementCount(command.EntitlementCount);
        licence.SetDeployedCount(command.DeployedCount);
    }

    private async Task<Asset> LoadAsync(Guid id, CancellationToken ct)
        => await _assets.GetAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(EntityType, id);

    private async Task<Asset> LoadWithHistoryAsync(Guid id, CancellationToken ct)
        => await _assets.GetWithHistoryAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(EntityType, id);

    private async Task<AssetDetailDto> ReloadAsync(Guid id, CancellationToken ct)
        => await _queries.GetAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(EntityType, id);
}
