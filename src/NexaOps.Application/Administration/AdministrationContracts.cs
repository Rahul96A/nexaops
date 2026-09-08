using NexaOps.Domain.Identity;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Application.Administration;

// ---------------------------------------------------------------------------
// Categories
// ---------------------------------------------------------------------------

public sealed record SubcategoryDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Guid? DefaultAssignmentGroupId,
    string? DefaultAssignmentGroupName,
    int SortOrder,
    bool IsActive,
    int RecordCount);

public sealed record CategoryDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    ServiceModule Module,
    Guid? DefaultAssignmentGroupId,
    string? DefaultAssignmentGroupName,
    int SortOrder,
    bool IsActive,
    int RecordCount,
    IReadOnlyList<SubcategoryDto> Subcategories,
    byte[]? RowVersion);

public sealed class UpsertCategoryCommand
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ServiceModule Module { get; set; } = ServiceModule.Incident;
    public Guid? DefaultAssignmentGroupId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[]? RowVersion { get; set; }
}

public sealed class UpsertSubcategoryCommand
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? DefaultAssignmentGroupId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

// ---------------------------------------------------------------------------
// Groups
// ---------------------------------------------------------------------------

public sealed record GroupMemberDto(
    Guid UserId,
    string DisplayName,
    string Email,
    string? JobTitle,
    string? AvatarColor,
    bool IsLead,
    bool IsActive);

public sealed record GroupAdminDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    GroupType Type,
    string? Email,
    Guid? ManagerUserId,
    string? ManagerName,
    Guid? DefaultAssigneeUserId,
    string? DefaultAssigneeName,
    Guid? BusinessCalendarId,
    string? BusinessCalendarName,
    bool IsActive,
    int MemberCount,
    IReadOnlyList<GroupMemberDto> Members,
    byte[]? RowVersion);

public sealed class UpsertGroupCommand
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public GroupType Type { get; set; } = GroupType.Assignment;
    public string? Email { get; set; }
    public Guid? ManagerUserId { get; set; }
    public Guid? DefaultAssigneeUserId { get; set; }
    public Guid? BusinessCalendarId { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[]? RowVersion { get; set; }
}

public sealed class SetGroupMemberCommand
{
    public Guid UserId { get; set; }
    public bool IsLead { get; set; }
}

// ---------------------------------------------------------------------------
// Users
// ---------------------------------------------------------------------------

public sealed record UserAdminDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string DisplayName,
    string? PhoneNumber,
    string? EmployeeId,
    string? JobTitle,
    Guid? OrganizationId,
    string? OrganizationName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? ManagerId,
    string? ManagerName,
    string? Location,
    string? TimeZoneId,
    string? Locale,
    UserStatus Status,
    bool IsServiceAccount,
    bool MustChangePassword,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<RoleSummaryDto> Roles,
    IReadOnlyList<string> Groups,
    byte[]? RowVersion);

public sealed record UserListItemDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? JobTitle,
    string? DepartmentName,
    UserStatus Status,
    string? AvatarColor,
    DateTimeOffset? LastLoginAt,
    int RoleCount);

public sealed class UpsertUserCommand
{
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? EmployeeId { get; set; }
    public string? JobTitle { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? ManagerId { get; set; }
    public string? Location { get; set; }
    public string? TimeZoneId { get; set; }
    public string? Locale { get; set; }
    public bool IsServiceAccount { get; set; }
    public byte[]? RowVersion { get; set; }
}

/// <summary>
/// The roles a user should hold, as a complete set.
/// <para>
/// A set rather than add/remove operations: two administrators editing the same user should
/// produce one of their intended outcomes, not an interleaving of both.
/// </para>
/// </summary>
public sealed class SetUserRolesCommand
{
    public IList<Guid> RoleIds { get; init; } = [];
}

public sealed class SetUserStatusCommand
{
    public UserStatus Status { get; set; }
}

/// <summary>
/// The outcome of creating a user, including the one-time password when local auth is in use.
/// </summary>
/// <param name="TemporaryPassword">
/// Returned exactly once, in the response to the request that created the account, and never
/// stored or retrievable afterwards. Null under federated authentication, where NexaOps issues
/// no credentials at all.
/// </param>
public sealed record CreatedUserDto(UserAdminDto User, string? TemporaryPassword);

// ---------------------------------------------------------------------------
// Roles
// ---------------------------------------------------------------------------

public sealed record RoleSummaryDto(Guid Id, string Code, string Name, bool IsSystem);

public sealed record RoleDetailDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsSystem,
    int UserCount,
    IReadOnlyList<string> Permissions,
    byte[]? RowVersion);

/// <param name="Code">Permission code, e.g. <c>incident.assign</c>.</param>
/// <param name="Category">Grouping for the editor.</param>
public sealed record PermissionDto(string Code, string Category, string Name, string Description);

public sealed class UpsertRoleCommand
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IList<string> Permissions { get; init; } = [];
    public byte[]? RowVersion { get; set; }
}
