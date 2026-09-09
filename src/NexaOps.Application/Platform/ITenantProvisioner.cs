using NexaOps.Domain.Identity;

namespace NexaOps.Application.Platform;

/// <summary>
/// Stands up everything a tenant needs before anyone can use it: roles and their permission
/// grants, the priority matrix, business calendars, SLA definitions and policies, record number
/// sequences and the change advisory board.
/// <para>
/// This is the same code path for a paying customer as for the demo seeder. There is deliberately
/// no separate "real customer" provisioning routine, because a second path is a path that gets
/// tested less and drifts.
/// </para>
/// </summary>
public interface ITenantProvisioner
{
    /// <summary>
    /// Creates the tenant if its code is not already taken, then provisions or tops up its
    /// baseline configuration. Returns the tenant either way.
    /// <para>
    /// Idempotent: re-running after a release that adds a permission or an SLA target brings an
    /// existing tenant up to date without disturbing anything an administrator has customised.
    /// Grants are only ever added, never removed.
    /// </para>
    /// </summary>
    Task<Tenant> ProvisionAsync(TenantProvisioningRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Everything needed to stand up a tenant.</summary>
public sealed class TenantProvisioningRequest
{
    /// <summary>Short identifier used in URLs and support conversations, e.g. <c>acme-in</c>.</summary>
    public required string Code { get; init; }

    public required string Name { get; init; }
    public string? LegalName { get; init; }
    public string? PrimaryDomain { get; init; }
    public TenantStatus Status { get; init; } = TenantStatus.Active;
}
