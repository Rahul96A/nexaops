using NexaOps.Application.Common;
using NexaOps.Domain.Identity;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Application.Administration;

/// <summary>Persistence for the things a tenant administrator configures.</summary>
public interface IAdministrationRepository
{
    // --- Categories ---
    Task<Category?> GetCategoryAsync(Guid id, CancellationToken ct = default);

    Task<bool> CategoryCodeExistsAsync(string code, ServiceModule module, Guid? exceptId, CancellationToken ct = default);

    Task<bool> SubcategoryCodeExistsAsync(Guid categoryId, string code, Guid? exceptId, CancellationToken ct = default);

    void AddCategory(Category category);

    void AddSubcategory(Subcategory subcategory);

    Task<Subcategory?> GetSubcategoryAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// How many records classify to this category, across every module that uses it.
    /// <para>
    /// Needed because a category in use cannot simply be removed: the records pointing at it
    /// would lose their classification. Deactivation is offered instead, and the count is what
    /// tells an administrator why.
    /// </para>
    /// </summary>
    Task<int> CountRecordsInCategoryAsync(Guid categoryId, CancellationToken ct = default);

    Task<int> CountRecordsInSubcategoryAsync(Guid subcategoryId, CancellationToken ct = default);

    void RemoveCategory(Category category);

    void RemoveSubcategory(Subcategory subcategory);

    // --- Groups ---
    Task<Group?> GetGroupAsync(Guid id, CancellationToken ct = default);

    Task<bool> GroupCodeExistsAsync(string code, Guid? exceptId, CancellationToken ct = default);

    void AddGroup(Group group);

    void AddMember(GroupMember member);

    void RemoveMember(GroupMember member);

    // --- Users ---
    Task<User?> GetUserAsync(Guid id, CancellationToken ct = default);

    Task<bool> EmailExistsAsync(string email, Guid? exceptId, CancellationToken ct = default);

    void AddUser(User user);

    void AddUserRole(UserRole userRole);

    void RemoveUserRole(UserRole userRole);

    /// <summary>The user's current role assignments, tracked so they can be replaced.</summary>
    Task<IReadOnlyList<UserRole>> GetUserRolesAsync(Guid userId, CancellationToken ct = default);

    // --- Roles ---
    Task<Role?> GetRoleAsync(Guid id, CancellationToken ct = default);

    Task<bool> RoleCodeExistsAsync(string code, Guid? exceptId, CancellationToken ct = default);

    Task<IReadOnlyList<Role>> GetRolesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    void AddRole(Role role);

    void AddRolePermission(RolePermission permission);

    void RemoveRolePermission(RolePermission permission);

    void RemoveRole(Role role);

    Task<int> CountUsersInRoleAsync(Guid roleId, CancellationToken ct = default);

    void SetExpectedVersion<T>(T entity, byte[] rowVersion) where T : class;
}

/// <summary>Read models for the administration screens.</summary>
public interface IAdministrationQueryService
{
    Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(ServiceModule? module, CancellationToken ct = default);

    Task<CategoryDto?> GetCategoryAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<GroupAdminDto>> GetGroupsAsync(CancellationToken ct = default);

    Task<GroupAdminDto?> GetGroupAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<UserListItemDto>> SearchUsersAsync(UserSearchQuery query, CancellationToken ct = default);

    Task<UserAdminDto?> GetUserAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<RoleDetailDto>> GetRolesAsync(CancellationToken ct = default);

    Task<RoleDetailDto?> GetRoleAsync(Guid id, CancellationToken ct = default);
}

public sealed class UserSearchQuery : PagedQuery
{
    public string? Search { get; set; }
    public UserStatus? Status { get; set; }
    public Guid? RoleId { get; set; }
    public Guid? GroupId { get; set; }
}
