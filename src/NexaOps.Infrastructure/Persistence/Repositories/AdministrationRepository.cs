using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Administration;
using NexaOps.Application.Common;
using NexaOps.Domain.Common;
using NexaOps.Domain.Identity;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Infrastructure.Persistence.Repositories;

/// <inheritdoc />
public sealed class AdministrationRepository : IAdministrationRepository
{
    private readonly NexaOpsDbContext _context;

    public AdministrationRepository(NexaOpsDbContext context) => _context = context;

    // --- Categories ---

    /// <inheritdoc />
    public async Task<Category?> GetCategoryAsync(Guid id, CancellationToken ct = default)
        => await _context.Categories
            .Include(c => c.Subcategories)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> CategoryCodeExistsAsync(
        string code,
        ServiceModule module,
        Guid? exceptId,
        CancellationToken ct = default)
        // Scoped to the module, because incident and change taxonomies are separate: "NETWORK"
        // meaning one thing to incidents and another to changes is legitimate.
        => await _context.Categories
            .AnyAsync(c => c.Code == code && c.Module == module && (exceptId == null || c.Id != exceptId), ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> SubcategoryCodeExistsAsync(
        Guid categoryId,
        string code,
        Guid? exceptId,
        CancellationToken ct = default)
        => await _context.Subcategories
            .AnyAsync(
                s => s.CategoryId == categoryId && s.Code == code && (exceptId == null || s.Id != exceptId),
                ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void AddCategory(Category category) => _context.Categories.Add(category);

    /// <inheritdoc />
    public void AddSubcategory(Subcategory subcategory) => _context.Subcategories.Add(subcategory);

    /// <inheritdoc />
    public async Task<Subcategory?> GetSubcategoryAsync(Guid id, CancellationToken ct = default)
        => await _context.Subcategories.FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<int> CountRecordsInCategoryAsync(Guid categoryId, CancellationToken ct = default)
    {
        // Every module that classifies, counted. A category used only by changes is still in
        // use, and reporting zero because incidents happen not to use it would let somebody
        // delete a classification the change history depends on.
        var incidents = await _context.Incidents
            .CountAsync(i => i.CategoryId == categoryId, ct).ConfigureAwait(false);

        var requests = await _context.ServiceRequests
            .CountAsync(r => r.CategoryId == categoryId, ct).ConfigureAwait(false);

        var problems = await _context.Problems
            .CountAsync(p => p.CategoryId == categoryId, ct).ConfigureAwait(false);

        var changes = await _context.Changes
            .CountAsync(c => c.CategoryId == categoryId, ct).ConfigureAwait(false);

        var catalogItems = await _context.CatalogItems
            .CountAsync(c => c.CategoryId == categoryId, ct).ConfigureAwait(false);

        return incidents + requests + problems + changes + catalogItems;
    }

    /// <inheritdoc />
    public async Task<int> CountRecordsInSubcategoryAsync(Guid subcategoryId, CancellationToken ct = default)
    {
        var incidents = await _context.Incidents
            .CountAsync(i => i.SubcategoryId == subcategoryId, ct).ConfigureAwait(false);

        var problems = await _context.Problems
            .CountAsync(p => p.SubcategoryId == subcategoryId, ct).ConfigureAwait(false);

        return incidents + problems;
    }

    /// <inheritdoc />
    public void RemoveCategory(Category category) => _context.Categories.Remove(category);

    /// <inheritdoc />
    public void RemoveSubcategory(Subcategory subcategory) => _context.Subcategories.Remove(subcategory);

    // --- Groups ---

    /// <inheritdoc />
    public async Task<Group?> GetGroupAsync(Guid id, CancellationToken ct = default)
        => await _context.Groups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == id, ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> GroupCodeExistsAsync(string code, Guid? exceptId, CancellationToken ct = default)
        => await _context.Groups
            .AnyAsync(g => g.Code == code && (exceptId == null || g.Id != exceptId), ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void AddGroup(Group group) => _context.Groups.Add(group);

    /// <inheritdoc />
    public void AddMember(GroupMember member) => _context.GroupMembers.Add(member);

    /// <inheritdoc />
    public void RemoveMember(GroupMember member) => _context.GroupMembers.Remove(member);

    // --- Users ---

    /// <inheritdoc />
    public async Task<User?> GetUserAsync(Guid id, CancellationToken ct = default)
        => await _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> EmailExistsAsync(string email, Guid? exceptId, CancellationToken ct = default)
        => await _context.Users
            .AnyAsync(u => u.Email == email && (exceptId == null || u.Id != exceptId), ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void AddUser(User user) => _context.Users.Add(user);

    /// <inheritdoc />
    public void AddUserRole(UserRole userRole) => _context.UserRoles.Add(userRole);

    /// <inheritdoc />
    public void RemoveUserRole(UserRole userRole) => _context.UserRoles.Remove(userRole);

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserRole>> GetUserRolesAsync(Guid userId, CancellationToken ct = default)
        => await _context.UserRoles
            .Where(ur => ur.UserId == userId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    // --- Roles ---

    /// <inheritdoc />
    public async Task<Role?> GetRoleAsync(Guid id, CancellationToken ct = default)
        => await _context.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> RoleCodeExistsAsync(string code, Guid? exceptId, CancellationToken ct = default)
        => await _context.Roles
            .AnyAsync(r => r.Code == code && (exceptId == null || r.Id != exceptId), ct)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Role>> GetRolesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return [];
        }

        // Tenant-filtered by the global query filter, so a role identifier from a neighbouring
        // tenant simply does not come back and the caller sees a count mismatch.
        return await _context.Roles
            .AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void AddRole(Role role) => _context.Roles.Add(role);

    /// <inheritdoc />
    public void AddRolePermission(RolePermission permission) => _context.RolePermissions.Add(permission);

    /// <inheritdoc />
    public void RemoveRolePermission(RolePermission permission) => _context.RolePermissions.Remove(permission);

    /// <inheritdoc />
    public void RemoveRole(Role role) => _context.Roles.Remove(role);

    /// <inheritdoc />
    public async Task<int> CountUsersInRoleAsync(Guid roleId, CancellationToken ct = default)
        => await _context.UserRoles.CountAsync(ur => ur.RoleId == roleId, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public void SetExpectedVersion<T>(T entity, byte[] rowVersion) where T : class
        => _context.Entry(entity).Property(nameof(TenantEntity.RowVersion)).OriginalValue = rowVersion;
}
