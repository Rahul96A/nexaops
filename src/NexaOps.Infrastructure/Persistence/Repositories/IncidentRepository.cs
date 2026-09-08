using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Incidents;
using NexaOps.Application.Sla;
using NexaOps.Domain.Platform;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Sla;

namespace NexaOps.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IIncidentRepository" />
public sealed class IncidentRepository : IIncidentRepository, IIncidentSlaWriter
{
    private readonly NexaOpsDbContext _context;

    public IncidentRepository(NexaOpsDbContext context) => _context = context;

    /// <inheritdoc />
    public Task<Incident?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.Incidents.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<Incident?> GetWithClocksAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var incident = await _context.Incidents
            .Include(i => i.Tags)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (incident is null)
        {
            return null;
        }

        // SLA instances are polymorphic across modules, so they are loaded explicitly with the
        // module discriminator rather than through the navigation, which would ignore it.
        var clocks = await _context.SlaInstances
            .Include(s => s.SlaDefinition)
            .Where(s => s.Module == ServiceModule.Incident && s.RecordId == id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        incident.SlaInstances.Clear();
        foreach (var clock in clocks)
        {
            incident.SlaInstances.Add(clock);
        }

        return incident;
    }

    /// <inheritdoc />
    public Task<Incident?> GetByNumberAsync(string number, CancellationToken cancellationToken = default)
        => _context.Incidents.FirstOrDefaultAsync(i => i.Number == number, cancellationToken);

    /// <inheritdoc />
    public void Add(Incident incident) => _context.Incidents.Add(incident);

    /// <inheritdoc />
    public void SetExpectedVersion(Incident incident, byte[] rowVersion)
        => _context.Entry(incident).Property(i => i.RowVersion).OriginalValue = rowVersion;

    /// <inheritdoc />
    public void AddComment(IncidentComment comment) => _context.IncidentComments.Add(comment);

    /// <inheritdoc />
    public void AddTag(IncidentTag tag) => _context.IncidentTags.Add(tag);

    /// <inheritdoc />
    public void RemoveTags(IEnumerable<IncidentTag> tags) => _context.IncidentTags.RemoveRange(tags);

    /// <inheritdoc />
    public void AddSlaInstance(SlaInstance instance) => _context.SlaInstances.Add(instance);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SlaInstance>> GetClocksNeedingAttentionAsync(
        DateTimeOffset asOf,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        // The SLA monitor genuinely spans tenants: one background pass must see every customer's
        // clocks. The scan is opened here and the monitor re-establishes a per-tenant scope
        // before it acts on any individual result.
        using var suppression = _context.SuppressTenantFilter();

        return await _context.SlaInstances
            .Include(s => s.SlaDefinition)
            .Where(s => s.State == SlaState.InProgress && s.DueAt <= asOf)
            .OrderBy(s => s.DueAt)
            .Take(Math.Clamp(maxResults, 1, 1000))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RefreshSlaRollUpAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        var incident = await _context.Incidents
            .FirstOrDefaultAsync(i => i.Id == incidentId, cancellationToken)
            .ConfigureAwait(false);

        if (incident is null)
        {
            return;
        }

        var clocks = await _context.SlaInstances
            .Where(s => s.Module == ServiceModule.Incident && s.RecordId == incidentId)
            .Select(s => new { s.State, s.DueAt, s.BreachedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        incident.HasBreachedSla = clocks.Any(c => c.BreachedAt is not null);

        var live = clocks
            .Where(c => c.State is SlaState.InProgress or SlaState.Paused)
            .Select(c => c.DueAt)
            .ToList();

        incident.NextSlaDueAt = live.Count == 0 ? null : live.Min();
    }
}

/// <inheritdoc />
public sealed class ServiceDeskReferenceRepository : IServiceDeskReferenceRepository
{
    private readonly NexaOpsDbContext _context;
    private readonly IApplicationCache _cache;
    private readonly ITenantContext _tenant;

    public ServiceDeskReferenceRepository(
        NexaOpsDbContext context,
        IApplicationCache cache,
        ITenantContext tenant)
    {
        _context = context;
        _cache = cache;
        _tenant = tenant;
    }

    /// <inheritdoc />
    public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default)
        => _context.Users.AnyAsync(u => u.Id == userId && !u.IsArchived, cancellationToken);

    /// <inheritdoc />
    public Task<bool> GroupExistsAsync(Guid groupId, CancellationToken cancellationToken = default)
        => _context.Groups.AnyAsync(g => g.Id == groupId && g.IsActive && !g.IsArchived, cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsMemberOfGroupAsync(
        Guid userId,
        Guid groupId,
        CancellationToken cancellationToken = default)
        => _context.GroupMembers.AnyAsync(m => m.UserId == userId && m.GroupId == groupId, cancellationToken);

    /// <inheritdoc />
    public Task<Category?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default)
        => _context.Categories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == categoryId && c.IsActive, cancellationToken);

    /// <inheritdoc />
    public Task<Subcategory?> GetSubcategoryAsync(Guid subcategoryId, CancellationToken cancellationToken = default)
        => _context.Subcategories.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == subcategoryId && s.IsActive, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriorityMatrixEntry>> GetPriorityMatrixAsync(
        CancellationToken cancellationToken = default)
    {
        // Sixteen rows read on every incident create and every impact change, edited perhaps
        // once a year. Exactly the shape of data that belongs in a cache.
        var cached = await _cache.GetOrCreateAsync(
            $"priority-matrix:{_tenant.TenantId}",
            async ct => await _context.PriorityMatrixEntries
                .AsNoTracking()
                .ToListAsync(ct)
                .ConfigureAwait(false),
            TimeSpan.FromMinutes(15),
            cancellationToken).ConfigureAwait(false);

        return cached;
    }

    /// <inheritdoc />
    public async Task<(Guid? OrganizationId, Guid? DepartmentId)> GetUserPlacementAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var placement = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.OrganizationId, u.DepartmentId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return (placement?.OrganizationId, placement?.DepartmentId);
    }

    /// <inheritdoc />
    public async Task<Guid?> GetManagerIdAsync(Guid userId, CancellationToken cancellationToken = default)
        // Tenant-filtered like every other read here, so a manager recorded across a tenant
        // boundary - which the write guard would not allow in the first place - reads as absent
        // rather than resolving to somebody else's directory.
        => await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.ManagerId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void AddNotification(Notification notification) => _context.Notifications.Add(notification);
}

/// <inheritdoc />
public sealed class NumberSequenceService : INumberSequenceService
{
    private readonly NexaOpsDbContext _context;
    private readonly ITenantContext _tenant;

    public NumberSequenceService(NexaOpsDbContext context, ITenantContext tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    /// <inheritdoc />
    public async Task<string> NextAsync(string key, CancellationToken cancellationToken = default)
    {
        var normalisedKey = key.Trim().ToUpperInvariant();

        var sequence = await _context.NumberSequences
            .FirstOrDefaultAsync(s => s.Key == normalisedKey, cancellationToken)
            .ConfigureAwait(false);

        if (sequence is null)
        {
            sequence = new NumberSequence
            {
                TenantId = _tenant.TenantId,
                Key = normalisedKey,
                Prefix = normalisedKey,
                NextValue = 1,
                PadWidth = 7
            };

            _context.NumberSequences.Add(sequence);
        }

        var value = sequence.NextValue;
        sequence.NextValue = value + 1;

        // The row carries a rowversion, so two concurrent allocations cannot both commit: the
        // loser gets a concurrency conflict rather than a duplicate number. Callers run this
        // inside a transaction with a retrying execution strategy.
        return sequence.Format(value);
    }
}
