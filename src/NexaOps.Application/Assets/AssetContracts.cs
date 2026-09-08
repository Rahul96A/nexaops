using NexaOps.Application.Common;
using NexaOps.Domain.Assets;

namespace NexaOps.Application.Assets;

public sealed record AssetSummaryDto(
    Guid Id,
    string Number,
    string AssetTag,
    string Name,
    AssetKind Kind,
    AssetStatus Status,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    Guid? AssignedToUserId,
    string? AssignedToName,
    string? Location,
    DateOnly? WarrantyExpiresOn,
    bool IsOutOfWarranty,
    DateOnly? RefreshDueOn,
    bool IsDueForRefresh,
    decimal? PurchaseCost,
    DateTimeOffset CreatedAt);

public sealed record AssetCustodyDto(
    Guid Id,
    Guid UserId,
    string UserName,
    DateTimeOffset AssignedAt,
    DateTimeOffset? ReturnedAt,
    string? AssignmentNote,
    string? ReturnNote);

public sealed record AssetDetailDto(
    Guid Id,
    string Number,
    string AssetTag,
    string Name,
    AssetKind Kind,
    AssetStatus Status,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    Guid? AssignedToUserId,
    string? AssignedToName,
    DateTimeOffset? AssignedAt,
    string? Location,
    DateOnly? PurchasedOn,
    decimal? PurchaseCost,
    string? Vendor,
    string? PurchaseOrderNumber,
    DateOnly? WarrantyExpiresOn,
    bool IsOutOfWarranty,
    int? UsefulLifeMonths,
    DateOnly? RefreshDueOn,
    bool IsDueForRefresh,
    Guid? ConfigurationItemId,
    string? ConfigurationItemName,
    DateOnly? DisposedOn,
    string? DisposalNotes,
    DateTimeOffset CreatedAt,

    /// <summary>Who has held it, newest first. What an audit or a security incident asks for.</summary>
    IReadOnlyList<AssetCustodyDto> CustodyHistory,
    byte[]? RowVersion);

public sealed record LicenceSummaryDto(
    Guid Id,
    string Number,
    string ProductName,
    string? Publisher,
    string? Version,
    LicenceModel Model,
    int EntitlementCount,
    int DeployedCount,
    int? AvailableEntitlements,
    int OverDeployedBy,
    ComplianceState Compliance,
    DateOnly? ExpiresOn,
    decimal? AnnualCost,
    string? Vendor,
    string? AgreementReference,
    string? Notes,
    DateTimeOffset CreatedAt);

/// <param name="OverDeployedSeats">Total seats in excess of entitlement across the estate.</param>
/// <param name="ExposureCost">
/// Indicative cost of closing that gap, null when the over-deployed licences carry no cost.
/// Deliberately indicative: real remediation is negotiated, not arithmetic.
/// </param>
public sealed record AssetSummaryCountsDto(
    int TotalAssets,
    int InStock,
    int Assigned,
    int InRepair,
    int DueForRefresh,
    int OutOfWarranty,
    decimal? TotalPurchaseCost,
    int Licences,
    int OverDeployedLicences,
    int ExpiredLicences,
    int OverDeployedSeats,
    decimal? ExposureCost);

// ---------------------------------------------------------------------------
// Commands
// ---------------------------------------------------------------------------

public sealed class UpsertAssetCommand
{
    public string AssetTag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AssetKind Kind { get; set; } = AssetKind.Hardware;
    public AssetStatus Status { get; set; } = AssetStatus.InStock;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? Location { get; set; }
    public DateOnly? PurchasedOn { get; set; }
    public decimal? PurchaseCost { get; set; }
    public string? Vendor { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public DateOnly? WarrantyExpiresOn { get; set; }
    public int? UsefulLifeMonths { get; set; }
    public Guid? ConfigurationItemId { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class AssignAssetCommand
{
    public Guid UserId { get; set; }
    public string? Note { get; set; }
}

public sealed class ReturnAssetCommand
{
    public string? Note { get; set; }

    /// <summary>Where it goes back to. A faulty item returns straight into repair.</summary>
    public AssetStatus ReturnTo { get; set; } = AssetStatus.InStock;
}

public sealed class DisposeAssetCommand
{
    public DateOnly DisposedOn { get; set; }
    public string? Notes { get; set; }
}

public sealed class UpsertLicenceCommand
{
    public string ProductName { get; set; } = string.Empty;
    public string? Publisher { get; set; }
    public string? Version { get; set; }
    public string? AgreementReference { get; set; }
    public LicenceModel Model { get; set; } = LicenceModel.PerUser;
    public int EntitlementCount { get; set; }
    public int DeployedCount { get; set; }
    public DateOnly? StartsOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public decimal? AnnualCost { get; set; }
    public string? Vendor { get; set; }
    public string? Notes { get; set; }
    public byte[]? RowVersion { get; set; }
}

public sealed class AssetQuery
{
    public string? Search { get; set; }
    public List<AssetStatus>? Statuses { get; set; }
    public List<AssetKind>? Kinds { get; set; }
    public Guid? AssignedToUserId { get; set; }

    /// <summary>Named scope, e.g. <c>assigned-to-me</c>, <c>due-refresh</c>, <c>out-of-warranty</c>.</summary>
    public string? Scope { get; set; }

    public string SortBy { get; set; } = "assetTag";
    public bool SortDescending { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

/// <summary>Write-side access to the asset register.</summary>
public interface IAssetRepository
{
    Task<Asset?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Loads an asset with its custody history, needed to close an open record.</summary>
    Task<Asset?> GetWithHistoryAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> TagExistsAsync(string assetTag, Guid? exceptId, CancellationToken cancellationToken = default);

    void Add(Asset asset);

    void AddAssignment(AssetAssignment assignment);

    void SetExpectedVersion(Asset asset, byte[] rowVersion);

    Task<SoftwareLicence?> GetLicenceAsync(Guid id, CancellationToken cancellationToken = default);

    void AddLicence(SoftwareLicence licence);

    void SetLicenceExpectedVersion(SoftwareLicence licence, byte[] rowVersion);
}

/// <summary>Read-side queries for assets and licences.</summary>
public interface IAssetQueryService
{
    Task<PagedResult<AssetSummaryDto>> SearchAsync(AssetQuery query, CancellationToken cancellationToken = default);

    Task<AssetDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LicenceSummaryDto>> GetLicencesAsync(CancellationToken cancellationToken = default);

    Task<LicenceSummaryDto?> GetLicenceAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AssetSummaryCountsDto> GetSummaryAsync(CancellationToken cancellationToken = default);
}
