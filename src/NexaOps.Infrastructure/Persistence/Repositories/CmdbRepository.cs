using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Cmdb;
using NexaOps.Application.Common;
using NexaOps.Domain.Cmdb;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Infrastructure.Persistence.Repositories;

/// <inheritdoc />
public sealed class CmdbRepository : ICmdbRepository
{
    private readonly NexaOpsDbContext _context;

    public CmdbRepository(NexaOpsDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<ConfigurationItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.ConfigurationItems
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> NameExistsAsync(
        string name,
        Guid? exceptId,
        CancellationToken cancellationToken = default)
        => await _context.ConfigurationItems
            .AnyAsync(c => c.Name == name && (exceptId == null || c.Id != exceptId), cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(ConfigurationItem item) => _context.ConfigurationItems.Add(item);

    /// <inheritdoc />
    public void SetExpectedVersion(ConfigurationItem item, byte[] rowVersion)
        => _context.Entry(item).Property(c => c.RowVersion).OriginalValue = rowVersion;

    /// <inheritdoc />
    public async Task<CiRelationship?> GetRelationshipAsync(
        Guid sourceId,
        Guid targetId,
        CiRelationshipType type,
        CancellationToken cancellationToken = default)
        => await _context.CiRelationships
            .FirstOrDefaultAsync(
                r => r.SourceId == sourceId && r.TargetId == targetId && r.Type == type,
                cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void AddRelationship(CiRelationship relationship) => _context.CiRelationships.Add(relationship);

    /// <inheritdoc />
    public void RemoveRelationship(CiRelationship relationship) => _context.CiRelationships.Remove(relationship);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CiRelationship>> GetAllRelationshipsAsync(
        CancellationToken cancellationToken = default)
        // Tenant-filtered by the global query filter, so the graph can never span a boundary.
        => await _context.CiRelationships
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

/// <inheritdoc />
public sealed class CmdbQueryService : ICmdbQueryService
{
    private readonly NexaOpsDbContext _context;
    private readonly IDateTimeProvider _clock;

    public CmdbQueryService(NexaOpsDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    /// <summary>
    /// The CMDB is readable by anyone in the tenant holding <c>cmdb.read</c>.
    /// <para>
    /// Deliberately not scoped to owners or support groups: an agent who cannot see what a
    /// failing server supports cannot judge how urgent the call is, which is the main reason to
    /// keep a CMDB at all.
    /// </para>
    /// </summary>
    private IQueryable<ConfigurationItem> VisibleItems() => _context.ConfigurationItems.AsNoTracking();

    /// <inheritdoc />
    public async Task<PagedResult<CiSummaryDto>> SearchAsync(
        CiQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var source = ApplyFilters(VisibleItems(), query, today);

        var total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0)
        {
            return PagedResult<CiSummaryDto>.Empty(query.Page, query.PageSize);
        }

        var items = await ApplySort(source, query)
            .Skip((Math.Max(query.Page, 1) - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToSummary(today))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<CiSummaryDto>(items, total, query.Page, query.PageSize);
    }

    /// <summary>
    /// Built per call because the out-of-support flag is relative to today, and a static
    /// expression would bake in the date the process started.
    /// </summary>
    private static Expression<Func<ConfigurationItem, CiSummaryDto>> ToSummary(DateOnly today)
        => c => new CiSummaryDto(
            c.Id, c.Number, c.Name, c.Type, c.Status, c.Criticality,
            c.Environment, c.Location,
            c.OwnerUserId, c.Owner!.DisplayName,
            c.SupportGroupId, c.SupportGroup!.Name,
            c.SupportExpiresOn,
            c.SupportExpiresOn != null && c.SupportExpiresOn < today,
            c.CreatedAt);

    /// <inheritdoc />
    public async Task<CiDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await VisibleItems()
            .Include(c => c.Owner)
            .Include(c => c.SupportGroup)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        // The whole edge list in one query. A CMDB edge list is thousands of rows, not millions,
        // and walking it hop by hop would be six round trips per record page.
        var edges = await _context.CiRelationships
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var impacts = ImpactAnalysis.WhatBreaksIf(item.Id, edges);
        var dependsOn = ImpactAnalysis.WhatItDependsOn(item.Id, edges);

        var referencedIds = impacts.Select(i => i.ItemId)
            .Concat(dependsOn.Select(d => d.ItemId))
            .Distinct()
            .ToList();

        var referenced = referencedIds.Count == 0
            ? []
            : await VisibleItems()
                .Where(c => referencedIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Number, c.Name, c.Type, c.Status, c.Criticality })
                .ToDictionaryAsync(c => c.Id, cancellationToken)
                .ConfigureAwait(false);

        IReadOnlyList<RelatedItemDto> Project(IReadOnlyList<ImpactedItem> walked) =>
            walked
                .Where(w => referenced.ContainsKey(w.ItemId))
                .Select(w =>
                {
                    var c = referenced[w.ItemId];
                    return new RelatedItemDto(
                        c.Id, c.Number, c.Name, c.Type, c.Status, c.Criticality,
                        w.ViaRelationship, w.Depth);
                })
                // Nearest first, then worst first: the reader wants to know who to call, and
                // that is the directly-affected critical item rather than a distant minor one.
                .OrderBy(r => r.Depth)
                .ThenByDescending(r => r.Criticality)
                .ToList();

        var openIncidents = await _context.Incidents
            .AsNoTracking()
            .CountAsync(
                i => i.ConfigurationItemId == item.Id
                     && (i.Status == IncidentStatus.New
                         || i.Status == IncidentStatus.Assigned
                         || i.Status == IncidentStatus.InProgress
                         || i.Status == IncidentStatus.Pending),
                cancellationToken)
            .ConfigureAwait(false);

        return new CiDetailDto(
            item.Id, item.Number, item.Name, item.Description, item.Type, item.Status,
            item.Criticality, item.Location, item.SerialNumber, item.Manufacturer, item.Model,
            item.Version, item.Environment,
            item.OwnerUserId, item.Owner?.DisplayName,
            item.SupportGroupId, item.SupportGroup?.Name,
            item.AcquiredOn, item.SupportExpiresOn, item.IsOutOfSupport(today), item.Vendor,
            item.CreatedAt,
            Project(impacts),
            Project(dependsOn),
            openIncidents,
            item.RowVersion);
    }

    /// <inheritdoc />
    public async Task<CmdbSummaryCountsDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var visible = VisibleItems();

        var total = await visible.CountAsync(cancellationToken).ConfigureAwait(false);

        var operational = await visible
            .CountAsync(c => c.Status == CiStatus.Operational, cancellationToken)
            .ConfigureAwait(false);

        var impaired = await visible
            .CountAsync(c => c.Status == CiStatus.Impaired, cancellationToken)
            .ConfigureAwait(false);

        var critical = await visible
            .CountAsync(c => c.Criticality == CiCriticality.Critical, cancellationToken)
            .ConfigureAwait(false);

        // Only live items count: a disposed server being out of support is not a problem.
        var outOfSupport = await visible
            .CountAsync(
                c => c.SupportExpiresOn != null
                     && c.SupportExpiresOn < today
                     && (c.Status == CiStatus.Operational || c.Status == CiStatus.Impaired),
                cancellationToken)
            .ConfigureAwait(false);

        var unowned = await visible
            .CountAsync(
                c => c.OwnerUserId == null
                     && (c.Status == CiStatus.Operational || c.Status == CiStatus.Impaired),
                cancellationToken)
            .ConfigureAwait(false);

        var byType = await visible
            .GroupBy(c => c.Type)
            .Select(g => new CiTypeCountDto(g.Key, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new CmdbSummaryCountsDto(
            total, operational, impaired, critical, outOfSupport, unowned, byType);
    }

    private static IQueryable<ConfigurationItem> ApplyFilters(
        IQueryable<ConfigurationItem> source,
        CiQuery query,
        DateOnly today)
    {
        source = query.Scope?.ToLowerInvariant() switch
        {
            "critical" => source.Where(c => c.Criticality == CiCriticality.Critical),
            "out-of-support" => source.Where(c =>
                c.SupportExpiresOn != null
                && c.SupportExpiresOn < today
                && (c.Status == CiStatus.Operational || c.Status == CiStatus.Impaired)),
            "unowned" => source.Where(c =>
                c.OwnerUserId == null
                && (c.Status == CiStatus.Operational || c.Status == CiStatus.Impaired)),
            "impaired" => source.Where(c => c.Status == CiStatus.Impaired),
            _ => source
        };

        if (query.Types is { Count: > 0 })
        {
            source = source.Where(c => query.Types.Contains(c.Type));
        }

        if (query.Statuses is { Count: > 0 })
        {
            source = source.Where(c => query.Statuses.Contains(c.Status));
        }

        if (query.Criticalities is { Count: > 0 })
        {
            source = source.Where(c => query.Criticalities.Contains(c.Criticality));
        }

        if (!string.IsNullOrWhiteSpace(query.Environment))
        {
            source = source.Where(c => c.Environment == query.Environment);
        }

        if (query.OwnerUserId is not null)
        {
            source = source.Where(c => c.OwnerUserId == query.OwnerUserId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();

            // Serial number is searched because "which machine is this" is often asked from the
            // sticker on the box rather than from anything in the record.
            source = source.Where(c =>
                c.Name.Contains(term)
                || c.Number.Contains(term)
                || (c.Description != null && c.Description.Contains(term))
                || (c.SerialNumber != null && c.SerialNumber.Contains(term)));
        }

        return source;
    }

    /// <summary>Sort fields are matched against a closed set by the service before this runs.</summary>
    private static IQueryable<ConfigurationItem> ApplySort(IQueryable<ConfigurationItem> source, CiQuery query)
        => (query.SortBy.ToLowerInvariant(), query.SortDescending) switch
        {
            ("number", true) => source.OrderByDescending(c => c.Number),
            ("number", false) => source.OrderBy(c => c.Number),
            ("type", true) => source.OrderByDescending(c => c.Type).ThenBy(c => c.Name),
            ("type", false) => source.OrderBy(c => c.Type).ThenBy(c => c.Name),
            ("criticality", false) => source.OrderBy(c => c.Criticality).ThenBy(c => c.Name),
            ("criticality", _) => source.OrderByDescending(c => c.Criticality).ThenBy(c => c.Name),
            ("status", true) => source.OrderByDescending(c => c.Status).ThenBy(c => c.Name),
            ("status", false) => source.OrderBy(c => c.Status).ThenBy(c => c.Name),
            ("supportexpireson", _) => source.OrderBy(c => c.SupportExpiresOn == null)
                .ThenBy(c => c.SupportExpiresOn),
            ("createdat", true) => source.OrderByDescending(c => c.CreatedAt),
            ("createdat", false) => source.OrderBy(c => c.CreatedAt),
            (_, true) => source.OrderByDescending(c => c.Name),
            _ => source.OrderBy(c => c.Name)
        };
}
