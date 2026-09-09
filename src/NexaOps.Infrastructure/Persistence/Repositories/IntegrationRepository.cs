using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Common;
using NexaOps.Application.Integration;
using NexaOps.Domain.Identity;
using NexaOps.Domain.Integration;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Infrastructure.Persistence.Repositories;

/// <inheritdoc />
public sealed class InboundEmailRepository : IInboundEmailRepository
{
    private readonly NexaOpsDbContext _context;

    public InboundEmailRepository(NexaOpsDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<InboundMessage?> FindByExternalIdAsync(
        string externalMessageId,
        CancellationToken ct = default)
        // Tenant-filtered by the global query filter, so one tenant's Message-ID cannot suppress
        // or thread into another's — which matters because a Message-ID is chosen by whoever
        // sent the mail, not by us.
        => await _context.InboundMessages
            .FirstOrDefaultAsync(m => m.ExternalMessageId == externalMessageId, ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(InboundMessage message) => _context.InboundMessages.Add(message);

    /// <inheritdoc />
    public async Task<PagedResult<InboundMessageDto>> SearchAsync(
        InboundMessageQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var source = _context.InboundMessages.AsNoTracking();

        if (query.Status is not null)
        {
            source = source.Where(m => m.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(m => m.Subject.Contains(term) || m.FromAddress.Contains(term));
        }

        var total = await source.CountAsync(ct).ConfigureAwait(false);

        if (total == 0)
        {
            return PagedResult<InboundMessageDto>.Empty(query.Page, query.PageSize);
        }

        var items = await source
            .OrderByDescending(m => m.ReceivedAt)
            .Skip((Math.Max(query.Page, 1) - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(m => new InboundMessageDto(
                m.Id,
                m.ExternalMessageId,
                m.FromAddress,
                m.FromDisplayName,
                m.Subject,
                m.BodyPreview,
                m.ReceivedAt,
                m.Status,
                m.RecordId,
                m.RecordNumber,
                m.Outcome))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<InboundMessageDto>(items, total, query.Page, query.PageSize);
    }
}

/// <inheritdoc />
public sealed class IntegrationDirectory : IIntegrationDirectory
{
    private readonly NexaOpsDbContext _context;

    public IntegrationDirectory(NexaOpsDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IntegrationUserDto?> FindActiveUserByEmailAsync(
        string email,
        CancellationToken ct = default)
    {
        var normalised = (email ?? string.Empty).Trim().ToLowerInvariant();

        if (normalised.Length == 0)
        {
            return null;
        }

        return await _context.Users
            .AsNoTracking()
            .Where(u => u.Email == normalised && u.Status == UserStatus.Active && !u.IsServiceAccount)
            .Select(u => new IntegrationUserDto(u.Id, u.Email, u.DisplayName))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IntegrationUserDto?> FindActiveUserByIdAsync(Guid userId, CancellationToken ct = default)
        => await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.Status == UserStatus.Active)
            .Select(u => new IntegrationUserDto(u.Id, u.Email, u.DisplayName))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IntegrationRecordDto?> FindIncidentByNumberAsync(
        string number,
        CancellationToken ct = default)
    {
        var normalised = (number ?? string.Empty).Trim().ToUpperInvariant();

        if (normalised.Length == 0)
        {
            return null;
        }

        return await _context.Incidents
            .AsNoTracking()
            .Where(i => i.Number == normalised)
            .Select(i => new IntegrationRecordDto(
                i.Id,
                i.Number,
                i.Status != IncidentStatus.Resolved
                && i.Status != IncidentStatus.Closed
                && i.Status != IncidentStatus.Cancelled))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }
}

/// <inheritdoc />
public sealed class IntegrationKeyRepository : IIntegrationKeyRepository
{
    /// <summary>
    /// How stale the last-used stamp is allowed to get before it is written again.
    /// <para>
    /// A busy integration calls constantly. Writing on every call would turn a read path into a
    /// write path for information nobody needs to the second.
    /// </para>
    /// </summary>
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    private readonly NexaOpsDbContext _context;

    public IntegrationKeyRepository(NexaOpsDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IntegrationKey?> FindByHashAsync(string keyHash, CancellationToken ct = default)
    {
        // Authentication happens before a tenant exists, so the filter is suppressed for exactly
        // this lookup — the key is what establishes the tenant that everything after it uses.
        using var _ = _context.SuppressTenantFilter();

        return await _context.IntegrationKeys
            .AsNoTracking()
            .Include(k => k.ServiceAccount)
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IntegrationKey>> GetForTenantAsync(CancellationToken ct = default)
        => await _context.IntegrationKeys
            .AsNoTracking()
            .Include(k => k.ServiceAccount)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IntegrationKey?> GetAsync(Guid id, CancellationToken ct = default)
        => await _context.IntegrationKeys.FirstOrDefaultAsync(k => k.Id == id, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public void Add(IntegrationKey key) => _context.IntegrationKeys.Add(key);

    /// <inheritdoc />
    public async Task TouchAsync(Guid keyId, DateTimeOffset now, CancellationToken ct = default)
    {
        using var _ = _context.SuppressTenantFilter();

        // A direct update rather than load-modify-save: this runs during authentication, outside
        // the request's unit of work, and must not enlist the key in a transaction that the
        // request might later roll back.
        await _context.IntegrationKeys
            .Where(k => k.Id == keyId
                        && (k.LastUsedAt == null || k.LastUsedAt < now.Subtract(TouchInterval)))
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now), ct)
            .ConfigureAwait(false);
    }
}
