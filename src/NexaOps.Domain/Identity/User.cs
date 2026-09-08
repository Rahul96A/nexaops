using NexaOps.Domain.Common;

namespace NexaOps.Domain.Identity;

/// <summary>
/// A person (or service principal) inside a tenant.
/// <para>
/// Credentials are only populated when Auth:Mode is Local. Under Entra ID the password
/// columns stay null and <see cref="ExternalObjectId"/> carries the Entra object id.
/// </para>
/// </summary>
public class User : TenantEntity
{
    /// <summary>Primary sign-in identifier. Unique per tenant, stored lower-cased.</summary>
    public string Email { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    /// <summary>Denormalised for list rendering and search; kept in sync by the user service.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>E.164, e.g. +919876543210.</summary>
    public string? PhoneNumber { get; set; }

    public string? EmployeeId { get; set; }
    public string? JobTitle { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? ManagerId { get; set; }
    public string? Location { get; set; }

    /// <summary>Overrides the tenant timezone for this user when set.</summary>
    public string? TimeZoneId { get; set; }
    public string? Locale { get; set; }

    public UserStatus Status { get; set; } = UserStatus.Active;

    /// <summary>True for automation identities. Service accounts cannot sign in interactively.</summary>
    public bool IsServiceAccount { get; set; }

    // --- Local credential material. Never leaves the server, never serialised into a DTO. ---
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Rotated whenever credentials or role assignments change. Access tokens carry the stamp,
    /// so a stale token is rejected immediately after a privilege change rather than at expiry.
    /// </summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public bool MustChangePassword { get; set; }
    public DateTimeOffset? PasswordChangedAt { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTimeOffset? LockoutEndsAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    // --- Federated identity. ---
    /// <summary>Entra ID oid claim, set on first federated sign-in.</summary>
    public string? ExternalObjectId { get; set; }
    public string? ExternalIssuer { get; set; }

    /// <summary>Deterministic avatar tint so the UI need not store an image for every user.</summary>
    public string? AvatarColor { get; set; }

    public Organization? Organization { get; set; }
    public Department? Department { get; set; }
    public User? Manager { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<GroupMember> GroupMemberships { get; set; } = new List<GroupMember>();

    public string FullName => string.IsNullOrWhiteSpace(DisplayName)
        ? (FirstName + " " + LastName).Trim()
        : DisplayName;

    /// <summary>An account can authenticate only when active and outside any lockout window.</summary>
    public bool CanSignIn(DateTimeOffset now)
        => Status == UserStatus.Active
           && !IsArchived
           && !IsServiceAccount
           && (LockoutEndsAt is null || LockoutEndsAt <= now);
}

public enum UserStatus
{
    Active = 1,
    Invited = 2,
    Disabled = 3,
    Locked = 4
}
