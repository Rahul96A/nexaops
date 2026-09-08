using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Administration;
using NexaOps.Application.Common;
using NexaOps.Domain.Identity;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Infrastructure.Persistence.Repositories;

/// <inheritdoc />
public sealed class AdministrationQueryService : IAdministrationQueryService
{
    private readonly NexaOpsDbContext _context;

    public AdministrationQueryService(NexaOpsDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(
        ServiceModule? module,
        CancellationToken ct = default)
    {
        var source = _context.Categories.AsNoTracking();

        if (module is not null)
        {
            source = source.Where(c => c.Module == module);
        }

        var categories = await source
            .Include(c => c.Subcategories)
            .OrderBy(c => c.Module)
            .ThenBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Usage counts are fetched in two grouped queries rather than one per row: an
        // administration screen with forty categories should not issue eighty round trips to
        // tell somebody which of them are safe to delete.
        var categoryUsage = await CategoryUsageAsync(ct).ConfigureAwait(false);
        var subcategoryUsage = await SubcategoryUsageAsync(ct).ConfigureAwait(false);

        var groupNames = await GroupNamesAsync(ct).ConfigureAwait(false);

        return
        [
            .. categories.Select(c => new CategoryDto(
                c.Id,
                c.Code,
                c.Name,
                c.Description,
                c.Module,
                c.DefaultAssignmentGroupId,
                Name(groupNames, c.DefaultAssignmentGroupId),
                c.SortOrder,
                c.IsActive,
                categoryUsage.GetValueOrDefault(c.Id),
                [
                    .. c.Subcategories
                        .OrderBy(s => s.SortOrder)
                        .ThenBy(s => s.Name)
                        .Select(s => new SubcategoryDto(
                            s.Id,
                            s.Code,
                            s.Name,
                            s.Description,
                            s.DefaultAssignmentGroupId,
                            Name(groupNames, s.DefaultAssignmentGroupId),
                            s.SortOrder,
                            s.IsActive,
                            subcategoryUsage.GetValueOrDefault(s.Id)))
                ],
                c.RowVersion))
        ];
    }

    /// <inheritdoc />
    public async Task<CategoryDto?> GetCategoryAsync(Guid id, CancellationToken ct = default)
        => (await GetCategoriesAsync(null, ct).ConfigureAwait(false)).FirstOrDefault(c => c.Id == id);

    /// <inheritdoc />
    public async Task<IReadOnlyList<GroupAdminDto>> GetGroupsAsync(CancellationToken ct = default)
    {
        var groups = await _context.Groups
            .AsNoTracking()
            .Include(g => g.Members).ThenInclude(m => m.User)
            .OrderBy(g => g.Type)
            .ThenBy(g => g.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var userNames = await UserNamesAsync(ct).ConfigureAwait(false);
        var calendarNames = await _context.BusinessCalendars
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct)
            .ConfigureAwait(false);

        return [.. groups.Select(g => ToDto(g, userNames, calendarNames))];
    }

    /// <inheritdoc />
    public async Task<GroupAdminDto?> GetGroupAsync(Guid id, CancellationToken ct = default)
    {
        var group = await _context.Groups
            .AsNoTracking()
            .Include(g => g.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(g => g.Id == id, ct)
            .ConfigureAwait(false);

        if (group is null)
        {
            return null;
        }

        var userNames = await UserNamesAsync(ct).ConfigureAwait(false);
        var calendarNames = await _context.BusinessCalendars
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct)
            .ConfigureAwait(false);

        return ToDto(group, userNames, calendarNames);
    }

    /// <inheritdoc />
    public async Task<PagedResult<UserListItemDto>> SearchUsersAsync(
        UserSearchQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var source = _context.Users.AsNoTracking();

        if (query.Status is not null)
        {
            source = source.Where(u => u.Status == query.Status);
        }

        if (query.RoleId is not null)
        {
            source = source.Where(u => u.UserRoles.Any(ur => ur.RoleId == query.RoleId));
        }

        if (query.GroupId is not null)
        {
            source = source.Where(u => u.GroupMemberships.Any(m => m.GroupId == query.GroupId));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();

            source = source.Where(u =>
                u.DisplayName.Contains(term)
                || u.Email.Contains(term)
                || u.EmployeeId!.Contains(term));
        }

        var total = await source.CountAsync(ct).ConfigureAwait(false);

        if (total == 0)
        {
            return PagedResult<UserListItemDto>.Empty(query.Page, query.PageSize);
        }

        var items = await source
            // Active people first: a directory sorted purely alphabetically buries the people
            // somebody is actually looking for behind years of leavers.
            .OrderBy(u => u.Status)
            .ThenBy(u => u.DisplayName)
            .Skip((Math.Max(query.Page, 1) - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(u => new UserListItemDto(
                u.Id,
                u.Email,
                u.DisplayName,
                u.JobTitle,
                u.Department!.Name,
                u.Status,
                u.AvatarColor,
                u.LastLoginAt,
                u.UserRoles.Count))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<UserListItemDto>(items, total, query.Page, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<UserAdminDto?> GetUserAsync(Guid id, CancellationToken ct = default)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Include(u => u.Organization)
            .Include(u => u.Department)
            .Include(u => u.Manager)
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.GroupMemberships).ThenInclude(m => m.Group)
            .FirstOrDefaultAsync(u => u.Id == id, ct)
            .ConfigureAwait(false);

        if (user is null)
        {
            return null;
        }

        // Note what is not projected: PasswordHash and SecurityStamp. Credential material never
        // leaves the server, and a DTO is exactly where it would leak from if it were mapped by
        // convention rather than by hand.
        return new UserAdminDto(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.DisplayName,
            user.PhoneNumber,
            user.EmployeeId,
            user.JobTitle,
            user.OrganizationId,
            user.Organization?.Name,
            user.DepartmentId,
            user.Department?.Name,
            user.ManagerId,
            user.Manager?.DisplayName,
            user.Location,
            user.TimeZoneId,
            user.Locale,
            user.Status,
            user.IsServiceAccount,
            user.MustChangePassword,
            user.LastLoginAt,
            [
                .. user.UserRoles
                    .Where(ur => ur.Role != null)
                    .Select(ur => new RoleSummaryDto(
                        ur.Role!.Id, ur.Role.Code, ur.Role.Name, ur.Role.IsSystem))
                    .OrderBy(r => r.Name)
            ],
            [.. user.GroupMemberships.Where(m => m.Group != null).Select(m => m.Group!.Name).Order()],
            user.RowVersion);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleDetailDto>> GetRolesAsync(CancellationToken ct = default)
    {
        var roles = await _context.Roles
            .AsNoTracking()
            .Include(r => r.RolePermissions)
            .OrderByDescending(r => r.IsSystem)
            .ThenBy(r => r.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var counts = await _context.UserRoles
            .AsNoTracking()
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, ct)
            .ConfigureAwait(false);

        return [.. roles.Select(r => ToDto(r, counts.GetValueOrDefault(r.Id)))];
    }

    /// <inheritdoc />
    public async Task<RoleDetailDto?> GetRoleAsync(Guid id, CancellationToken ct = default)
    {
        var role = await _context.Roles
            .AsNoTracking()
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            .ConfigureAwait(false);

        if (role is null)
        {
            return null;
        }

        var users = await _context.UserRoles
            .AsNoTracking()
            .CountAsync(ur => ur.RoleId == id, ct)
            .ConfigureAwait(false);

        return ToDto(role, users);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static RoleDetailDto ToDto(Role role, int userCount) => new(
        role.Id,
        role.Code,
        role.Name,
        role.Description,
        role.IsSystem,
        userCount,
        [.. role.RolePermissions.Select(p => p.PermissionCode).Order()],
        role.RowVersion);

    private static GroupAdminDto ToDto(
        Group group,
        IReadOnlyDictionary<Guid, string> userNames,
        IReadOnlyDictionary<Guid, string> calendarNames) => new(
        group.Id,
        group.Code,
        group.Name,
        group.Description,
        group.Type,
        group.Email,
        group.ManagerUserId,
        Name(userNames, group.ManagerUserId),
        group.DefaultAssigneeUserId,
        Name(userNames, group.DefaultAssigneeUserId),
        group.BusinessCalendarId,
        Name(calendarNames, group.BusinessCalendarId),
        group.IsActive,
        group.Members.Count,
        [
            .. group.Members
                .Where(m => m.User != null)
                .Select(m => new GroupMemberDto(
                    m.UserId,
                    m.User!.DisplayName,
                    m.User.Email,
                    m.User.JobTitle,
                    m.User.AvatarColor,
                    m.IsLead,

                    // Leavers stay visible in the membership list rather than disappearing from
                    // it: a group that still lists somebody who has gone is a fact worth seeing.
                    m.User.Status == UserStatus.Active))
                .OrderByDescending(m => m.IsLead)
                .ThenBy(m => m.DisplayName)
        ],
        group.RowVersion);

    private async Task<Dictionary<Guid, int>> CategoryUsageAsync(CancellationToken ct)
    {
        var usage = new Dictionary<Guid, int>();

        await AccumulateAsync(_context.Incidents.Where(i => i.CategoryId != null)
            .GroupBy(i => i.CategoryId!.Value), usage, ct).ConfigureAwait(false);

        await AccumulateAsync(_context.ServiceRequests.Where(r => r.CategoryId != null)
            .GroupBy(r => r.CategoryId!.Value), usage, ct).ConfigureAwait(false);

        await AccumulateAsync(_context.Problems.Where(p => p.CategoryId != null)
            .GroupBy(p => p.CategoryId!.Value), usage, ct).ConfigureAwait(false);

        await AccumulateAsync(_context.Changes.Where(c => c.CategoryId != null)
            .GroupBy(c => c.CategoryId!.Value), usage, ct).ConfigureAwait(false);

        await AccumulateAsync(_context.CatalogItems.Where(c => c.CategoryId != null)
            .GroupBy(c => c.CategoryId!.Value), usage, ct).ConfigureAwait(false);

        return usage;
    }

    private async Task<Dictionary<Guid, int>> SubcategoryUsageAsync(CancellationToken ct)
    {
        var usage = new Dictionary<Guid, int>();

        await AccumulateAsync(_context.Incidents.Where(i => i.SubcategoryId != null)
            .GroupBy(i => i.SubcategoryId!.Value), usage, ct).ConfigureAwait(false);

        await AccumulateAsync(_context.Problems.Where(p => p.SubcategoryId != null)
            .GroupBy(p => p.SubcategoryId!.Value), usage, ct).ConfigureAwait(false);

        return usage;
    }

    private static async Task AccumulateAsync<T>(
        IQueryable<IGrouping<Guid, T>> grouped,
        Dictionary<Guid, int> into,
        CancellationToken ct)
    {
        var counts = await grouped
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var row in counts)
        {
            into[row.Key] = into.GetValueOrDefault(row.Key) + row.Count;
        }
    }

    private Task<Dictionary<Guid, string>> GroupNamesAsync(CancellationToken ct)
        => _context.Groups.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Name, ct);

    private Task<Dictionary<Guid, string>> UserNamesAsync(CancellationToken ct)
        => _context.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

    private static string? Name(IReadOnlyDictionary<Guid, string> names, Guid? id)
        => id is not null && names.TryGetValue(id.Value, out var name) ? name : null;
}
