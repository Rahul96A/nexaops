using NexaOps.Application.Common;
using NexaOps.Domain.Identity;

namespace NexaOps.Application.Platform;

/// <summary>
/// Data access for platform administration, which is the one part of the product that
/// legitimately reads and writes across tenant boundaries.
/// <para>
/// It is a separate interface from <c>IAdministrationRepository</c> precisely so that crossing
/// tenants is something a service has to ask for by taking this dependency, rather than
/// something that could happen by accident inside a repository a tenant-scoped service already
/// holds. The implementation is the only place outside authentication that suppresses the tenant
/// filter, and every method here is reachable only from a caller holding a <c>platform.*</c>
/// permission.
/// </para>
/// </summary>
public interface IPlatformRepository
{
    Task<PagedResult<TenantSummaryDto>> SearchTenantsAsync(TenantQuery query, CancellationToken ct = default);

    Task<TenantDetailDto?> GetTenantAsync(Guid id, CancellationToken ct = default);

    /// <summary>The tracked entity, for a status change or a settings edit.</summary>
    Task<Tenant?> GetTenantEntityAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// True when a tenant already uses this code. Checked case-insensitively against the
    /// normalised form, because the code is matched that way at sign-in.
    /// </summary>
    Task<bool> TenantCodeExistsAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// True when this email address is already a user in the given tenant. Sign-in disambiguates
    /// by tenant, so the same address may legitimately exist in several.
    /// </summary>
    Task<bool> UserEmailExistsInTenantAsync(Guid tenantId, string email, CancellationToken ct = default);

    /// <summary>The tenant's role with this code, or null. Used to find Tenant Administrator.</summary>
    Task<Role?> GetTenantRoleByCodeAsync(Guid tenantId, string roleCode, CancellationToken ct = default);

    /// <summary>
    /// Adds a user and their role assignment on behalf of another tenant.
    /// <para>
    /// Ordinary user creation goes through <c>IUserAdminService</c> inside the tenant's own
    /// scope. This exists only for the first administrator, who has to be created before anybody
    /// in that tenant can sign in to create anybody.
    /// </para>
    /// </summary>
    void AddUserForTenant(User user, UserRole role);

    /// <summary>
    /// Revokes every unexpired refresh token belonging to a tenant, returning how many were
    /// revoked. Called when a tenant is suspended or closed, so that access ends when the
    /// current access tokens expire rather than when the refresh tokens would have.
    /// </summary>
    Task<int> RevokeTenantRefreshTokensAsync(Guid tenantId, string reason, CancellationToken ct = default);

    /// <summary>Persists pending changes across the tenant boundary.</summary>
    Task SaveCrossTenantAsync(CancellationToken ct = default);
}
