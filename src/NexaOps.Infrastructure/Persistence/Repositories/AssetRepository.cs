using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Assets;
using NexaOps.Application.Common;
using NexaOps.Domain.Assets;

namespace NexaOps.Infrastructure.Persistence.Repositories;

/// <inheritdoc />
public sealed class AssetRepository : IAssetRepository
{
    private readonly NexaOpsDbContext _context;

    public AssetRepository(NexaOpsDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<Asset?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.Assets
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Asset?> GetWithHistoryAsync(Guid id, CancellationToken cancellationToken = default)
        // With history, because issuing, returning and disposing all close the open custody
        // record, and an unloaded collection would leave it open for ever.
        => await _context.Assets
            .Include(a => a.AssignmentHistory)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> TagExistsAsync(
        string assetTag,
        Guid? exceptId,
        CancellationToken cancellationToken = default)
        => await _context.Assets
            .AnyAsync(a => a.AssetTag == assetTag && (exceptId == null || a.Id != exceptId), cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(Asset asset) => _context.Assets.Add(asset);

    /// <inheritdoc />
    public void AddAssignment(AssetAssignment assignment) => _context.AssetAssignments.Add(assignment);

    /// <inheritdoc />
    public void SetExpectedVersion(Asset asset, byte[] rowVersion)
        => _context.Entry(asset).Property(a => a.RowVersion).OriginalValue = rowVersion;

    /// <inheritdoc />
    public async Task<SoftwareLicence?> GetLicenceAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.SoftwareLicences
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void AddLicence(SoftwareLicence licence) => _context.SoftwareLicences.Add(licence);

    /// <inheritdoc />
    public void SetLicenceExpectedVersion(SoftwareLicence licence, byte[] rowVersion)
        => _context.Entry(licence).Property(l => l.RowVersion).OriginalValue = rowVersion;
}

/// <inheritdoc />
public sealed class AssetQueryService : IAssetQueryService
{
    private readonly NexaOpsDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public AssetQueryService(
        NexaOpsDbContext context,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    private DateOnly Today() => DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

    private IQueryable<Asset> VisibleAssets() => _context.Assets.AsNoTracking();

    /// <summary>
    /// Built per call because the warranty and refresh flags are relative to today, and a static
    /// expression would bake in the date the process started.
    /// </summary>
    private static Expression<Func<Asset, AssetSummaryDto>> ToSummary(DateOnly today)
        => a => new AssetSummaryDto(
            a.Id, a.Number, a.AssetTag, a.Name, a.Kind, a.Status,
            a.Manufacturer, a.Model, a.SerialNumber,
            a.AssignedToUserId, a.AssignedTo!.DisplayName,
            a.Location,
            a.WarrantyExpiresOn,
            a.WarrantyExpiresOn != null && a.WarrantyExpiresOn < today,

            // The refresh date is computed in SQL here rather than read from the domain
            // property, because the property cannot be translated into a query. The two agree:
            // both are null unless a purchase date and a positive useful life exist.
            a.PurchasedOn != null && a.UsefulLifeMonths != null && a.UsefulLifeMonths > 0
                ? a.PurchasedOn.Value.AddMonths(a.UsefulLifeMonths.Value)
                : null,
            a.PurchasedOn != null && a.UsefulLifeMonths != null && a.UsefulLifeMonths > 0
                && a.PurchasedOn.Value.AddMonths(a.UsefulLifeMonths.Value) <= today,
            a.PurchaseCost,
            a.CreatedAt);

    /// <inheritdoc />
    public async Task<PagedResult<AssetSummaryDto>> SearchAsync(
        AssetQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var today = Today();
        var source = ApplyFilters(VisibleAssets(), query, today);

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0)
        {
            return PagedResult<AssetSummaryDto>.Empty(query.Page, query.PageSize);
        }

        var items = await ApplySort(source, query)
            .Skip((Math.Max(query.Page, 1) - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToSummary(today))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<AssetSummaryDto>(items, total, query.Page, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<AssetDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var asset = await VisibleAssets()
            .Include(a => a.AssignedTo)
            .Include(a => a.ConfigurationItem)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (asset is null)
        {
            return null;
        }

        var today = Today();

        var history = await _context.AssetAssignments
            .AsNoTracking()
            .Where(h => h.AssetId == id)
            .OrderByDescending(h => h.AssignedAt)
            .Select(h => new AssetCustodyDto(
                h.Id, h.UserId, h.User!.DisplayName,
                h.AssignedAt, h.ReturnedAt, h.AssignmentNote, h.ReturnNote))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AssetDetailDto(
            asset.Id, asset.Number, asset.AssetTag, asset.Name, asset.Kind, asset.Status,
            asset.Manufacturer, asset.Model, asset.SerialNumber,
            asset.AssignedToUserId, asset.AssignedTo?.DisplayName, asset.AssignedAt,
            asset.Location, asset.PurchasedOn, asset.PurchaseCost, asset.Vendor,
            asset.PurchaseOrderNumber, asset.WarrantyExpiresOn, asset.IsOutOfWarranty(today),
            asset.UsefulLifeMonths, asset.RefreshDueOn, asset.IsDueForRefresh(today),
            asset.ConfigurationItemId, asset.ConfigurationItem?.Name,
            asset.DisposedOn, asset.DisposalNotes, asset.CreatedAt,
            history,
            asset.RowVersion);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LicenceSummaryDto>> GetLicencesAsync(
        CancellationToken cancellationToken = default)
    {
        var licences = await _context.SoftwareLicences
            .AsNoTracking()
            .OrderBy(l => l.ProductName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Projected in memory because the compliance position is domain logic, and duplicating
        // its rules in a SQL expression is how the two come to disagree.
        return licences.Select(ToLicenceSummary).ToList();
    }

    /// <inheritdoc />
    public async Task<LicenceSummaryDto?> GetLicenceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var licence = await _context.SoftwareLicences
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return licence is null ? null : ToLicenceSummary(licence);
    }

    private LicenceSummaryDto ToLicenceSummary(SoftwareLicence l)
        => new(l.Id, l.Number, l.ProductName, l.Publisher, l.Version, l.Model,
            l.EntitlementCount, l.DeployedCount, l.AvailableEntitlements, l.OverDeployedBy,
            l.ComplianceAt(Today()), l.ExpiresOn, l.AnnualCost, l.Vendor,
            l.AgreementReference, l.Notes, l.CreatedAt);

    /// <inheritdoc />
    public async Task<AssetSummaryCountsDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var today = Today();
        var visible = VisibleAssets();

        var total = await visible.CountAsync(cancellationToken).ConfigureAwait(false);

        var inStock = await visible
            .CountAsync(a => a.Status == AssetStatus.InStock, cancellationToken).ConfigureAwait(false);

        var assigned = await visible
            .CountAsync(a => a.Status == AssetStatus.Assigned, cancellationToken).ConfigureAwait(false);

        var inRepair = await visible
            .CountAsync(a => a.Status == AssetStatus.InRepair, cancellationToken).ConfigureAwait(false);

        // Only assets still in service count towards refresh and warranty: a disposed laptop
        // being out of warranty is not a problem anybody needs to see.
        var dueForRefresh = await visible
            .CountAsync(
                a => (a.Status == AssetStatus.InStock || a.Status == AssetStatus.Assigned
                      || a.Status == AssetStatus.InRepair)
                     && a.PurchasedOn != null
                     && a.UsefulLifeMonths != null
                     && a.UsefulLifeMonths > 0
                     && a.PurchasedOn.Value.AddMonths(a.UsefulLifeMonths.Value) <= today,
                cancellationToken)
            .ConfigureAwait(false);

        var outOfWarranty = await visible
            .CountAsync(
                a => (a.Status == AssetStatus.InStock || a.Status == AssetStatus.Assigned
                      || a.Status == AssetStatus.InRepair)
                     && a.WarrantyExpiresOn != null
                     && a.WarrantyExpiresOn < today,
                cancellationToken)
            .ConfigureAwait(false);

        var totalCost = await visible
            .Where(a => a.Status != AssetStatus.Disposed)
            .SumAsync(a => a.PurchaseCost, cancellationToken)
            .ConfigureAwait(false);

        var licences = await _context.SoftwareLicences
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var overDeployed = licences.Where(l => l.ComplianceAt(today) == ComplianceState.OverDeployed).ToList();

        // Indicative only. Real remediation is negotiated, not arithmetic - so the figure is
        // labelled as exposure rather than presented as a bill.
        var exposure = overDeployed
            .Where(l => l.AnnualCost is not null && l.EntitlementCount > 0)
            .Sum(l => l.AnnualCost!.Value / l.EntitlementCount * l.OverDeployedBy);

        return new AssetSummaryCountsDto(
            total, inStock, assigned, inRepair, dueForRefresh, outOfWarranty, totalCost,
            licences.Count,
            overDeployed.Count,
            licences.Count(l => l.ComplianceAt(today) == ComplianceState.Expired),
            overDeployed.Sum(l => l.OverDeployedBy),
            exposure == 0 ? null : Math.Round(exposure, 2));
    }

    private IQueryable<Asset> ApplyFilters(IQueryable<Asset> source, AssetQuery query, DateOnly today)
    {
        var userId = _currentUser.UserIdOrNull ?? Guid.Empty;

        source = query.Scope?.ToLowerInvariant() switch
        {
            "assigned-to-me" => source.Where(a => a.AssignedToUserId == userId),
            "in-stock" => source.Where(a => a.Status == AssetStatus.InStock),
            "in-repair" => source.Where(a => a.Status == AssetStatus.InRepair),
            "due-refresh" => source.Where(a =>
                (a.Status == AssetStatus.InStock || a.Status == AssetStatus.Assigned
                 || a.Status == AssetStatus.InRepair)
                && a.PurchasedOn != null
                && a.UsefulLifeMonths != null
                && a.UsefulLifeMonths > 0
                && a.PurchasedOn.Value.AddMonths(a.UsefulLifeMonths.Value) <= today),
            "out-of-warranty" => source.Where(a =>
                (a.Status == AssetStatus.InStock || a.Status == AssetStatus.Assigned
                 || a.Status == AssetStatus.InRepair)
                && a.WarrantyExpiresOn != null
                && a.WarrantyExpiresOn < today),
            _ => source
        };

        if (query.Statuses is { Count: > 0 })
        {
            source = source.Where(a => query.Statuses.Contains(a.Status));
        }

        if (query.Kinds is { Count: > 0 })
        {
            source = source.Where(a => query.Kinds.Contains(a.Kind));
        }

        if (query.AssignedToUserId is not null)
        {
            source = source.Where(a => a.AssignedToUserId == query.AssignedToUserId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();

            // Serial number and tag are searched because the question is usually asked from the
            // sticker on the box rather than from anything in the record.
            source = source.Where(a =>
                a.AssetTag.Contains(term)
                || a.Name.Contains(term)
                || a.Number.Contains(term)
                || (a.SerialNumber != null && a.SerialNumber.Contains(term)));
        }

        return source;
    }

    /// <summary>Sort fields are matched against a closed set by the service before this runs.</summary>
    private static IQueryable<Asset> ApplySort(IQueryable<Asset> source, AssetQuery query)
        => (query.SortBy.ToLowerInvariant(), query.SortDescending) switch
        {
            ("name", true) => source.OrderByDescending(a => a.Name),
            ("name", false) => source.OrderBy(a => a.Name),
            ("number", true) => source.OrderByDescending(a => a.Number),
            ("number", false) => source.OrderBy(a => a.Number),
            ("status", true) => source.OrderByDescending(a => a.Status).ThenBy(a => a.AssetTag),
            ("status", false) => source.OrderBy(a => a.Status).ThenBy(a => a.AssetTag),
            ("kind", true) => source.OrderByDescending(a => a.Kind).ThenBy(a => a.AssetTag),
            ("kind", false) => source.OrderBy(a => a.Kind).ThenBy(a => a.AssetTag),
            ("purchasedon", _) => source.OrderBy(a => a.PurchasedOn == null).ThenBy(a => a.PurchasedOn),
            ("warrantyexpireson", _) => source.OrderBy(a => a.WarrantyExpiresOn == null)
                .ThenBy(a => a.WarrantyExpiresOn),
            ("createdat", true) => source.OrderByDescending(a => a.CreatedAt),
            ("createdat", false) => source.OrderBy(a => a.CreatedAt),
            (_, true) => source.OrderByDescending(a => a.AssetTag),
            _ => source.OrderBy(a => a.AssetTag)
        };
}
