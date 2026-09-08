using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Requests;
using NexaOps.Domain.Approvals;
using NexaOps.Domain.Catalog;
using NexaOps.Domain.Requests;

namespace NexaOps.Infrastructure.Persistence.Repositories;

/// <inheritdoc />
public sealed class RequestRepository : IRequestRepository
{
    private readonly NexaOpsDbContext _context;

    public RequestRepository(NexaOpsDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<ServiceRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.ServiceRequests
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ServiceRequest?> GetWithLinesAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.ServiceRequests
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ServiceRequest?> GetByNumberAsync(string number, CancellationToken cancellationToken = default)
        => await _context.ServiceRequests
            .FirstOrDefaultAsync(r => r.Number == number, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(ServiceRequest request) => _context.ServiceRequests.Add(request);

    /// <inheritdoc />
    public void AddItem(RequestItem item) => _context.RequestItems.Add(item);

    /// <inheritdoc />
    public void AddComment(RequestComment comment) => _context.RequestComments.Add(comment);

    /// <inheritdoc />
    public void AddApproval(Approval approval) => _context.Approvals.Add(approval);

    /// <inheritdoc />
    public void SetExpectedVersion(ServiceRequest request, byte[] rowVersion)
        // OriginalValue, not the property. Assigning request.RowVersion would compile and do
        // nothing, because EF compares the version the change tracker loaded.
        => _context.Entry(request).Property(r => r.RowVersion).OriginalValue = rowVersion;

    /// <inheritdoc />
    public async Task RefreshSlaRollUpAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await _context.ServiceRequests
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            .ConfigureAwait(false);

        if (request is null)
        {
            return;
        }

        // Clocks are addressed by module and record id, never through a navigation, so this
        // stays correct for every module that uses the SLA engine.
        var clocks = await _context.SlaInstances
            .AsNoTracking()
            .Where(s => s.Module == Domain.ServiceDesk.ServiceModule.Request && s.RecordId == requestId)
            .Select(s => new { s.State, s.DueAt, s.BreachedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        request.HasBreachedSla = clocks.Any(c => c.BreachedAt is not null);

        request.NextSlaDueAt = clocks
            .Where(c => c.State == Domain.Sla.SlaState.InProgress)
            .Select(c => (DateTimeOffset?)c.DueAt)
            .DefaultIfEmpty(null)
            .Min();
    }
}

/// <inheritdoc />
public sealed class CatalogRepository : ICatalogRepository
{
    private readonly NexaOpsDbContext _context;

    public CatalogRepository(NexaOpsDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<CatalogItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.CatalogItems
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<CatalogItem?> GetWithVariablesAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.CatalogItems
            .Include(c => c.Variables)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogItem>> GetManyWithVariablesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return [];
        }

        var idList = ids.ToList();

        // One round trip for a whole basket. Loading each line's item separately would be N+1
        // on the hottest path in the module.
        return await _context.CatalogItems
            .Include(c => c.Variables)
            .Where(c => idList.Contains(c.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> CodeExistsAsync(
        string code,
        Guid? exceptId,
        CancellationToken cancellationToken = default)
        => await _context.CatalogItems
            .AnyAsync(c => c.Code == code && (exceptId == null || c.Id != exceptId), cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(CatalogItem item) => _context.CatalogItems.Add(item);

    /// <inheritdoc />
    public void AddVariable(CatalogItemVariable variable) => _context.CatalogItemVariables.Add(variable);

    /// <inheritdoc />
    public void RemoveVariables(IEnumerable<CatalogItemVariable> variables)
        => _context.CatalogItemVariables.RemoveRange(variables);

    /// <inheritdoc />
    public void SetExpectedVersion(CatalogItem item, byte[] rowVersion)
        => _context.Entry(item).Property(c => c.RowVersion).OriginalValue = rowVersion;
}

/// <inheritdoc />
public sealed class ApprovalRepository : IApprovalRepository
{
    private readonly NexaOpsDbContext _context;

    public ApprovalRepository(NexaOpsDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<Approval?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.Approvals
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Approval>> GetForRecordAsync(
        string module,
        Guid recordId,
        CancellationToken cancellationToken = default)
        // Tracked, not AsNoTracking: the caller settles peers in the same stage and expects
        // those changes to be saved with the unit of work.
        => await _context.Approvals
            .Where(a => a.Module == module && a.RecordId == recordId)
            .OrderBy(a => a.Stage)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(Approval approval) => _context.Approvals.Add(approval);
}
