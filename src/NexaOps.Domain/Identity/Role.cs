using NexaOps.Domain.Common;

namespace NexaOps.Domain.Identity;

/// <summary>
/// A named bundle of permissions. Roles exist for administrative convenience only - no code
/// path anywhere in NexaOps branches on a role name. Enforcement always asks for a permission.
/// </summary>
public class Role : TenantEntity
{
    /// <summary>Stable identifier, e.g. service-desk-agent. Unique per tenant.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>System roles are provisioned with every tenant and cannot be renamed or deleted.</summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// Grants permissions across every tenant. Reserved for the platform operator; a tenant
    /// administrator can neither create nor assign a role with this flag set.
    /// </summary>
    public bool IsPlatformScoped { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}

/// <summary>Grant of a single permission to a role.</summary>
public class RolePermission : TenantEntity
{
    public Guid RoleId { get; set; }

    /// <summary>
    /// Permission code from the application-layer catalogue, e.g. incident.assign. Stored as a
    /// string rather than a foreign key so that adding a permission in a release does not
    /// require a data migration for every tenant.
    /// </summary>
    public string PermissionCode { get; set; } = string.Empty;

    public Role? Role { get; set; }
}

/// <summary>Assignment of a role to a user.</summary>
public class UserRole : TenantEntity
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }

    /// <summary>Optional expiry, for temporary elevation such as on-call change approval rights.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public User? User { get; set; }
    public Role? Role { get; set; }

    public bool IsEffective(DateTimeOffset now) => ExpiresAt is null || ExpiresAt > now;
}
