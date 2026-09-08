using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexaOps.Api.Authorization;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Security;
using NexaOps.Domain.Identity;
using NexaOps.Domain.Localisation;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Infrastructure.Persistence;

namespace NexaOps.Api.Controllers;

/// <summary>
/// Look-up data the forms need: categories, groups, agents, the priority matrix and Indian
/// reference data. Every response is tenant-scoped by the persistence layer.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/reference")]
[Asp.Versioning.ApiVersion("1.0")]
[Produces("application/json")]
public sealed class ReferenceDataController : ControllerBase
{
    private readonly NexaOpsDbContext _context;
    private readonly ICurrentUser _currentUser;

    public ReferenceDataController(NexaOpsDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    /// <summary>Active categories, with their subcategories, for a module.</summary>
    [HttpGet("categories")]
    [RequiresPermission(Permissions.CategoryRead)]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> Categories(
        [FromQuery] ServiceModule module = ServiceModule.Incident,
        CancellationToken cancellationToken = default)
    {
        var categories = await _context.Categories
            .AsNoTracking()
            .Where(c => c.Module == module && c.IsActive && !c.IsArchived)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(
                c.Id,
                c.Code,
                c.Name,
                c.DefaultAssignmentGroupId,
                c.Subcategories
                    .Where(s => s.IsActive && !s.IsArchived)
                    .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
                    .Select(s => new SubcategoryDto(s.Id, s.Code, s.Name, s.DefaultAssignmentGroupId))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return Ok(categories);
    }

    /// <summary>Active assignment groups.</summary>
    [HttpGet("groups")]
    [RequiresPermission(Permissions.GroupRead)]
    [ProducesResponseType(typeof(IReadOnlyList<GroupDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GroupDto>>> Groups(
        [FromQuery] GroupType type = GroupType.Assignment,
        CancellationToken cancellationToken = default)
    {
        var groups = await _context.Groups
            .AsNoTracking()
            .Where(g => g.Type == type && g.IsActive && !g.IsArchived)
            .OrderBy(g => g.Name)
            .Select(g => new GroupDto(g.Id, g.Code, g.Name, g.Email, g.Members.Count))
            .ToListAsync(cancellationToken);

        return Ok(groups);
    }

    /// <summary>
    /// Members of one group, for the assignee picker. Restricting the picker to group members
    /// mirrors the rule the incident service enforces on save.
    /// </summary>
    [HttpGet("groups/{groupId:guid}/members")]
    [RequiresPermission(Permissions.GroupRead)]
    [ProducesResponseType(typeof(IReadOnlyList<UserSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserSummaryDto>>> GroupMembers(
        Guid groupId,
        CancellationToken cancellationToken)
    {
        var members = await _context.GroupMembers
            .AsNoTracking()
            .Where(m => m.GroupId == groupId)
            .Join(
                _context.Users.AsNoTracking().Where(u => !u.IsArchived),
                m => m.UserId,
                u => u.Id,
                (m, u) => new UserSummaryDto(
                    u.Id, u.DisplayName, u.Email, u.JobTitle, u.AvatarColor, m.IsLead))
            .OrderBy(u => u.DisplayName)
            .ToListAsync(cancellationToken);

        return Ok(members);
    }

    /// <summary>
    /// Searches users for a picker. Requires <c>user.read</c>, is capped at 25 results, and
    /// requires at least two characters so it cannot be used to enumerate the whole directory.
    /// </summary>
    [HttpGet("users")]
    [RequiresPermission(Permissions.UserRead)]
    [ProducesResponseType(typeof(IReadOnlyList<UserSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserSummaryDto>>> SearchUsers(
        [FromQuery] string? search,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(search) || search.Trim().Length < 2)
        {
            return Ok(Array.Empty<UserSummaryDto>());
        }

        var term = search.Trim();

        var users = await _context.Users
            .AsNoTracking()
            .Where(u => !u.IsArchived
                        && !u.IsServiceAccount
                        && (u.DisplayName.Contains(term)
                            || u.Email.Contains(term)
                            || (u.EmployeeId != null && u.EmployeeId.Contains(term))))
            .OrderBy(u => u.DisplayName)
            .Take(25)
            .Select(u => new UserSummaryDto(
                u.Id, u.DisplayName, u.Email, u.JobTitle, u.AvatarColor, false))
            .ToListAsync(cancellationToken);

        return Ok(users);
    }

    /// <summary>
    /// The tenant's impact-by-urgency priority matrix, so the UI can show the derived priority
    /// as the user changes impact or urgency without a round trip per keystroke.
    /// </summary>
    [HttpGet("priority-matrix")]
    [RequiresPermission(Permissions.CategoryRead)]
    [ProducesResponseType(typeof(IReadOnlyList<PriorityMatrixDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PriorityMatrixDto>>> PriorityMatrix(
        CancellationToken cancellationToken)
    {
        var entries = await _context.PriorityMatrixEntries
            .AsNoTracking()
            .OrderBy(e => e.Impact).ThenBy(e => e.Urgency)
            .Select(e => new PriorityMatrixDto(e.Impact, e.Urgency, e.Priority))
            .ToListAsync(cancellationToken);

        // Fall back to the product default when a tenant has not been seeded, so the form still
        // shows a correct derived priority rather than nothing.
        if (entries.Count == 0)
        {
            entries = PriorityCalculator.DefaultMatrix()
                .Select(m => new PriorityMatrixDto(m.Impact, m.Urgency, m.Priority))
                .ToList();
        }

        return Ok(entries);
    }

    /// <summary>
    /// Indian states with their GST codes. Static reference data used by address forms;
    /// available to any authenticated user.
    /// </summary>
    [HttpGet("india/states")]
    [ProducesResponseType(typeof(IReadOnlyList<IndianStateDto>), StatusCodes.Status200OK)]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    public ActionResult<IReadOnlyList<IndianStateDto>> IndianStates()
        => Ok(IndiaReference.States
            .Select(s => new IndianStateDto(s.Code, s.Name, s.GstStateCode, s.IsUnionTerritory))
            .ToList());

    /// <summary>The caller's own assignment groups, used by the "my team" queue filters.</summary>
    [HttpGet("my-groups")]
    [ProducesResponseType(typeof(IReadOnlyList<GroupDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GroupDto>>> MyGroups(CancellationToken cancellationToken)
    {
        var groupIds = _currentUser.GroupIds.ToList();

        var groups = await _context.Groups
            .AsNoTracking()
            .Where(g => groupIds.Contains(g.Id) && g.IsActive && !g.IsArchived)
            .OrderBy(g => g.Name)
            .Select(g => new GroupDto(g.Id, g.Code, g.Name, g.Email, g.Members.Count))
            .ToListAsync(cancellationToken);

        return Ok(groups);
    }
}

public sealed record CategoryDto(
    Guid Id,
    string Code,
    string Name,
    Guid? DefaultAssignmentGroupId,
    IReadOnlyList<SubcategoryDto> Subcategories);

public sealed record SubcategoryDto(Guid Id, string Code, string Name, Guid? DefaultAssignmentGroupId);

public sealed record GroupDto(Guid Id, string Code, string Name, string? Email, int MemberCount);

public sealed record UserSummaryDto(
    Guid Id,
    string DisplayName,
    string Email,
    string? JobTitle,
    string? AvatarColor,
    bool IsLead);

public sealed record PriorityMatrixDto(Impact Impact, Urgency Urgency, Priority Priority);

public sealed record IndianStateDto(string Code, string Name, string GstStateCode, bool IsUnionTerritory);
