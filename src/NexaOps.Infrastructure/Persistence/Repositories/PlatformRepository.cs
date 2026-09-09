using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Platform;
using NexaOps.Domain.Identity;

namespace NexaOps.Infrastructure.Persistence.Repositories;

/// <inheritdoc />
/// <remarks>
/// Every method suppresses the tenant query filter, because platform administration is the one
/// part of the product whose job is to see across tenants. That is safe only because the service
/// above demands a <c>platform.*</c> permission before calling anything here, and because no
/// tenant-scoped service takes this dependency.
/// <para>
/// <c>Tenant</c> itself is not <see cref="Domain.Common.ITenantOwned"/> and carries no filter;
/// the suppression is for the <c>User</c> and <c>Role</c> rows read and written alongside it,
/// and for the write guard in the save interceptor, which would otherwise refuse rows belonging
/// to a tenant that is not the ambient one.
/// </para>
/// </remarks>
public sealed class PlatformRepository : IPlatformRepository
{
    private readonly NexaOpsDbContext _context;

    public PlatformRepository(NexaOpsDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<PagedResult<TenantSummaryDto>> SearchTenantsAsync(
        TenantQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        using var suppression = _context.SuppressTenantFilter();

        var tenants = _context.Tenants.AsNoTracking();

        if (query.Status is not null)
        {
            tenants = tenants.Where(t => t.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();

            tenants = tenants.Where(t =>
                EF.Functions.Like(t.Code, $"%{term}%")
                || EF.Functions.Like(t.Name, $"%{term}%")
                || (t.LegalName != null && EF.Functions.Like(t.LegalName, $"%{term}%")));
        }

        var total = await tenants.CountAsync(ct).ConfigureAwait(false);

        // Newest first: the tenant somebody is looking for straight after onboarding it is the
        // one they just created.
        var page = await tenants
            .OrderByDescending(t => t.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(t => new TenantSummaryDto(
                t.Id,
                t.Code,
                t.Name,
                t.LegalName,
                t.Status.ToString(),
                t.PrimaryDomain,
                t.DataRegion,
                t.CreatedAt,
                _context.Users.Count(u => u.TenantId == t.Id && !u.IsArchived),
                _context.Users.Count(u =>
                    u.TenantId == t.Id && !u.IsArchived && u.Status == UserStatus.Active)))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<TenantSummaryDto>(page, total, query.Page, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<TenantDetailDto?> GetTenantAsync(Guid id, CancellationToken ct = default)
    {
        using var suppression = _context.SuppressTenantFilter();

        return await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TenantDetailDto(
                t.Id,
                t.Code,
                t.Name,
                t.LegalName,
                t.Status.ToString(),
                t.PrimaryDomain,
                t.EntraTenantId,
                t.TimeZoneId,
                t.Locale,
                t.CurrencyCode,
                t.DateFormat,
                t.DataRegion,
                t.RecordRetentionDays,
                t.AuditRetentionDays,
                t.CreatedAt,
                _context.Users.Count(u => u.TenantId == t.Id && !u.IsArchived),
                _context.Users.Count(u =>
                    u.TenantId == t.Id && !u.IsArchived && u.Status == UserStatus.Active),

                // Whether anybody has ever actually used the tenant. An onboarded customer who
                // has never signed in is the single most useful thing to see in this list.
                _context.Users
                    .Where(u => u.TenantId == t.Id && u.LastLoginAt != null)
                    .Max(u => u.LastLoginAt)))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Tenant?> GetTenantEntityAsync(Guid id, CancellationToken ct = default)
    {
        using var suppression = _context.SuppressTenantFilter();

        return await _context.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> TenantCodeExistsAsync(string code, CancellationToken ct = default)
    {
        using var suppression = _context.SuppressTenantFilter();

        return await _context.Tenants.AnyAsync(t => t.Code == code, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> UserEmailExistsInTenantAsync(
        Guid tenantId,
        string email,
        CancellationToken ct = default)
    {
        using var suppression = _context.SuppressTenantFilter();

        return await _context.Users
            .AnyAsync(u => u.TenantId == tenantId && u.Email == email, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Role?> GetTenantRoleByCodeAsync(
        Guid tenantId,
        string roleCode,
        CancellationToken ct = default)
    {
        using var suppression = _context.SuppressTenantFilter();

        return await _context.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Code == roleCode && !r.IsArchived, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void AddUserForTenant(User user, UserRole role)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(role);

        _context.Users.Add(user);
        _context.UserRoles.Add(role);
    }

    /// <inheritdoc />
    public async Task<int> RevokeTenantRefreshTokensAsync(
        Guid tenantId,
        string reason,
        CancellationToken ct = default)
    {
        using var suppression = _context.SuppressTenantFilter();

        var now = DateTimeOffset.UtcNow;

        // ExecuteUpdate rather than loading: a tenant can have any number of live sessions, and
        // the point of this call is that it finishes before the status change commits.
        return await _context.RefreshTokens
            .Where(t => t.TenantId == tenantId && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.RevokedAt, now)
                    .SetProperty(t => t.RevokedReason, reason),
                ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveCrossTenantAsync(CancellationToken ct = default)
    {
        // The suppression covers the save itself, not just the queries: without it the write
        // guard in the interceptor refuses every row whose TenantId is not the ambient tenant,
        // which for platform administration is all of them.
        using var suppression = _context.SuppressTenantFilter();

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
