using Microsoft.AspNetCore.Mvc;
using NexaOps.Api.Authorization;
using NexaOps.Application.Administration;
using NexaOps.Application.Common;
using NexaOps.Application.Security;
using NexaOps.Domain.Identity;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Api.Controllers;

/// <summary>
/// The tenant's own configuration: people, roles, groups and the taxonomy work is classified by.
/// <para>
/// Everything here is tenant-scoped. There is no endpoint that reaches across tenants and no way
/// to award a tenant permissions the platform has not granted it — a tenant administrator
/// administers their tenant, which is not the same job as administering the platform.
/// </para>
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/admin")]
[Asp.Versioning.ApiVersion("1.0")]
[Produces("application/json")]
public sealed class AdministrationController : ControllerBase
{
    private readonly IUserAdminService _users;
    private readonly IRoleAdminService _roles;
    private readonly ITaxonomyAdminService _taxonomy;

    public AdministrationController(
        IUserAdminService users,
        IRoleAdminService roles,
        ITaxonomyAdminService taxonomy)
    {
        _users = users;
        _roles = roles;
        _taxonomy = taxonomy;
    }

    // -----------------------------------------------------------------
    // Users
    // -----------------------------------------------------------------

    /// <summary>The tenant directory, active people first.</summary>
    [HttpGet("users")]
    [RequiresPermission(Permissions.UserRead)]
    [ProducesResponseType(typeof(PagedResult<UserListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<UserListItemDto>>> Users(
        [FromQuery] UserSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(await _users.SearchAsync(request.ToQuery(), cancellationToken));
    }

    /// <summary>One person, with their roles and group memberships.</summary>
    [HttpGet("users/{id:guid}")]
    [RequiresPermission(Permissions.UserRead)]
    [ProducesResponseType(typeof(UserAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserAdminDto>> GetUser(Guid id, CancellationToken cancellationToken)
        => Ok(await _users.GetAsync(id, cancellationToken));

    /// <summary>
    /// Creates an account.
    /// <para>
    /// Under local authentication the response carries a one-time password. It is returned in
    /// this response only: it is never stored in the clear, never written to the audit trail,
    /// and cannot be retrieved afterwards. Under federated authentication no credential is
    /// issued at all.
    /// </para>
    /// </summary>
    [HttpPost("users")]
    [RequiresPermission(Permissions.UserManage)]
    [ProducesResponseType(typeof(CreatedUserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CreatedUserDto>> CreateUser(
        [FromBody] UpsertUserCommand command,
        CancellationToken cancellationToken)
    {
        var created = await _users.CreateAsync(command, cancellationToken);

        return CreatedAtAction(
            nameof(GetUser),
            new { id = created.User.Id, version = "1.0" },
            created);
    }

    [HttpPut("users/{id:guid}")]
    [RequiresPermission(Permissions.UserManage)]
    [ProducesResponseType(typeof(UserAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserAdminDto>> UpdateUser(
        Guid id,
        [FromBody] UpsertUserCommand command,
        CancellationToken cancellationToken)
        => Ok(await _users.UpdateAsync(id, command, cancellationToken));

    /// <summary>
    /// Replaces the user's roles with exactly this set.
    /// <para>
    /// Held behind <c>role.manage</c> rather than <c>user.manage</c>: granting a role grants its
    /// permissions, which belongs with whoever is trusted to define them, not with whoever
    /// maintains job titles. Every existing session of the affected user ends immediately.
    /// </para>
    /// </summary>
    [HttpPut("users/{id:guid}/roles")]
    [RequiresPermission(Permissions.RoleManage)]
    [ProducesResponseType(typeof(UserAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UserAdminDto>> SetUserRoles(
        Guid id,
        [FromBody] SetUserRolesCommand command,
        CancellationToken cancellationToken)
        => Ok(await _users.SetRolesAsync(id, command, cancellationToken));

    /// <summary>Enables, disables or suspends an account. Disabling ends its sessions at once.</summary>
    [HttpPost("users/{id:guid}/status")]
    [RequiresPermission(Permissions.UserManage)]
    [ProducesResponseType(typeof(UserAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UserAdminDto>> SetUserStatus(
        Guid id,
        [FromBody] SetUserStatusCommand command,
        CancellationToken cancellationToken)
        => Ok(await _users.SetStatusAsync(id, command, cancellationToken));

    // -----------------------------------------------------------------
    // Roles
    // -----------------------------------------------------------------

    [HttpGet("roles")]
    [RequiresPermission(Permissions.RoleRead)]
    [ProducesResponseType(typeof(IReadOnlyList<RoleDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RoleDetailDto>>> Roles(CancellationToken cancellationToken)
        => Ok(await _roles.GetRolesAsync(cancellationToken));

    [HttpGet("roles/{id:guid}")]
    [RequiresPermission(Permissions.RoleRead)]
    [ProducesResponseType(typeof(RoleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoleDetailDto>> Role(Guid id, CancellationToken cancellationToken)
        => Ok(await _roles.GetAsync(id, cancellationToken));

    /// <summary>
    /// Every permission a tenant role may grant.
    /// <para>
    /// Published so the editor offers exactly what the server accepts. Platform permissions are
    /// withheld: a tenant cannot hold or assign them, so listing them would only advertise a
    /// boundary it is on the wrong side of.
    /// </para>
    /// </summary>
    [HttpGet("permissions")]
    [RequiresPermission(Permissions.RoleRead)]
    [ProducesResponseType(typeof(IReadOnlyList<PermissionDto>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<PermissionDto>> PermissionCatalogue()
        => Ok(_roles.GetPermissions());

    [HttpPost("roles")]
    [RequiresPermission(Permissions.RoleManage)]
    [ProducesResponseType(typeof(RoleDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RoleDetailDto>> CreateRole(
        [FromBody] UpsertRoleCommand command,
        CancellationToken cancellationToken)
    {
        var created = await _roles.CreateAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Role), new { id = created.Id, version = "1.0" }, created);
    }

    [HttpPut("roles/{id:guid}")]
    [RequiresPermission(Permissions.RoleManage)]
    [ProducesResponseType(typeof(RoleDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RoleDetailDto>> UpdateRole(
        Guid id,
        [FromBody] UpsertRoleCommand command,
        CancellationToken cancellationToken)
        => Ok(await _roles.UpdateAsync(id, command, cancellationToken));

    /// <summary>Deletes a role nobody holds. Built-in roles cannot be deleted.</summary>
    [HttpDelete("roles/{id:guid}")]
    [RequiresPermission(Permissions.RoleManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DeleteRole(Guid id, CancellationToken cancellationToken)
    {
        await _roles.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    // -----------------------------------------------------------------
    // Categories
    // -----------------------------------------------------------------

    [HttpGet("categories")]
    [RequiresPermission(Permissions.CategoryRead)]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> Categories(
        [FromQuery] ServiceModule? module,
        CancellationToken cancellationToken)
        => Ok(await _taxonomy.GetCategoriesAsync(module, cancellationToken));

    [HttpPost("categories")]
    [RequiresPermission(Permissions.CategoryManage)]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CategoryDto>> CreateCategory(
        [FromBody] UpsertCategoryCommand command,
        CancellationToken cancellationToken)
        => Ok(await _taxonomy.CreateCategoryAsync(command, cancellationToken));

    [HttpPut("categories/{id:guid}")]
    [RequiresPermission(Permissions.CategoryManage)]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CategoryDto>> UpdateCategory(
        Guid id,
        [FromBody] UpsertCategoryCommand command,
        CancellationToken cancellationToken)
        => Ok(await _taxonomy.UpdateCategoryAsync(id, command, cancellationToken));

    /// <summary>
    /// Deletes a category nothing classifies to.
    /// <para>
    /// A category in use is refused rather than cascaded: deleting it would strip the
    /// classification from records that already have one and silently change reporting for the
    /// period they were raised in. Deactivation does what people actually mean.
    /// </para>
    /// </summary>
    [HttpDelete("categories/{id:guid}")]
    [RequiresPermission(Permissions.CategoryManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken cancellationToken)
    {
        await _taxonomy.DeleteCategoryAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("categories/{id:guid}/subcategories")]
    [RequiresPermission(Permissions.CategoryManage)]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryDto>> AddSubcategory(
        Guid id,
        [FromBody] UpsertSubcategoryCommand command,
        CancellationToken cancellationToken)
        => Ok(await _taxonomy.AddSubcategoryAsync(id, command, cancellationToken));

    [HttpPut("subcategories/{id:guid}")]
    [RequiresPermission(Permissions.CategoryManage)]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryDto>> UpdateSubcategory(
        Guid id,
        [FromBody] UpsertSubcategoryCommand command,
        CancellationToken cancellationToken)
        => Ok(await _taxonomy.UpdateSubcategoryAsync(id, command, cancellationToken));

    [HttpDelete("subcategories/{id:guid}")]
    [RequiresPermission(Permissions.CategoryManage)]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CategoryDto>> DeleteSubcategory(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await _taxonomy.DeleteSubcategoryAsync(id, cancellationToken));

    // -----------------------------------------------------------------
    // Groups
    // -----------------------------------------------------------------

    [HttpGet("groups")]
    [RequiresPermission(Permissions.GroupRead)]
    [ProducesResponseType(typeof(IReadOnlyList<GroupAdminDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GroupAdminDto>>> Groups(CancellationToken cancellationToken)
        => Ok(await _taxonomy.GetGroupsAsync(cancellationToken));

    [HttpGet("groups/{id:guid}")]
    [RequiresPermission(Permissions.GroupRead)]
    [ProducesResponseType(typeof(GroupAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GroupAdminDto>> Group(Guid id, CancellationToken cancellationToken)
        => Ok(await _taxonomy.GetGroupAsync(id, cancellationToken));

    [HttpPost("groups")]
    [RequiresPermission(Permissions.GroupManage)]
    [ProducesResponseType(typeof(GroupAdminDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GroupAdminDto>> CreateGroup(
        [FromBody] UpsertGroupCommand command,
        CancellationToken cancellationToken)
    {
        var created = await _taxonomy.CreateGroupAsync(command, cancellationToken);
        return CreatedAtAction(nameof(Group), new { id = created.Id, version = "1.0" }, created);
    }

    [HttpPut("groups/{id:guid}")]
    [RequiresPermission(Permissions.GroupManage)]
    [ProducesResponseType(typeof(GroupAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GroupAdminDto>> UpdateGroup(
        Guid id,
        [FromBody] UpsertGroupCommand command,
        CancellationToken cancellationToken)
        => Ok(await _taxonomy.UpdateGroupAsync(id, command, cancellationToken));

    /// <summary>Adds somebody to a group, or changes whether they lead it.</summary>
    [HttpPut("groups/{id:guid}/members")]
    [RequiresPermission(Permissions.GroupManage)]
    [ProducesResponseType(typeof(GroupAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GroupAdminDto>> SetMember(
        Guid id,
        [FromBody] SetGroupMemberCommand command,
        CancellationToken cancellationToken)
        => Ok(await _taxonomy.SetMemberAsync(id, command, cancellationToken));

    [HttpDelete("groups/{id:guid}/members/{userId:guid}")]
    [RequiresPermission(Permissions.GroupManage)]
    [ProducesResponseType(typeof(GroupAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<GroupAdminDto>> RemoveMember(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken)
        => Ok(await _taxonomy.RemoveMemberAsync(id, userId, cancellationToken));
}

/// <summary>Query-string binding for the directory.</summary>
public sealed class UserSearchRequest
{
    public string? Search { get; set; }
    public UserStatus? Status { get; set; }
    public Guid? RoleId { get; set; }
    public Guid? GroupId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = PagedQuery.DefaultPageSize;

    public UserSearchQuery ToQuery() => new()
    {
        Search = Search,
        Status = Status,
        RoleId = RoleId,
        GroupId = GroupId,
        Page = Page,
        PageSize = PageSize
    };
}
