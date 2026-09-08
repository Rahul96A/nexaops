using FluentValidation;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Incidents;
using NexaOps.Application.Security;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Common;
using NexaOps.Domain.Identity;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Application.Administration;

/// <summary>Categories, subcategories and assignment groups: how work is classified and routed.</summary>
public interface ITaxonomyAdminService
{
    Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(ServiceModule? module, CancellationToken ct = default);

    Task<CategoryDto> CreateCategoryAsync(UpsertCategoryCommand command, CancellationToken ct = default);

    Task<CategoryDto> UpdateCategoryAsync(Guid id, UpsertCategoryCommand command, CancellationToken ct = default);

    /// <summary>Removes a category that nothing classifies to. Deactivation is the alternative.</summary>
    Task DeleteCategoryAsync(Guid id, CancellationToken ct = default);

    Task<CategoryDto> AddSubcategoryAsync(
        Guid categoryId,
        UpsertSubcategoryCommand command,
        CancellationToken ct = default);

    Task<CategoryDto> UpdateSubcategoryAsync(
        Guid id,
        UpsertSubcategoryCommand command,
        CancellationToken ct = default);

    Task<CategoryDto> DeleteSubcategoryAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<GroupAdminDto>> GetGroupsAsync(CancellationToken ct = default);

    Task<GroupAdminDto> GetGroupAsync(Guid id, CancellationToken ct = default);

    Task<GroupAdminDto> CreateGroupAsync(UpsertGroupCommand command, CancellationToken ct = default);

    Task<GroupAdminDto> UpdateGroupAsync(Guid id, UpsertGroupCommand command, CancellationToken ct = default);

    Task<GroupAdminDto> SetMemberAsync(Guid groupId, SetGroupMemberCommand command, CancellationToken ct = default);

    Task<GroupAdminDto> RemoveMemberAsync(Guid groupId, Guid userId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class TaxonomyAdminService : ITaxonomyAdminService
{
    private readonly IAdministrationRepository _admin;
    private readonly IAdministrationQueryService _queries;
    private readonly IServiceDeskReferenceRepository _reference;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<TaxonomyAdminService> _logger;

    public TaxonomyAdminService(
        IAdministrationRepository admin,
        IAdministrationQueryService queries,
        IServiceDeskReferenceRepository reference,
        IAuditService audit,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ILogger<TaxonomyAdminService> logger)
    {
        _admin = admin;
        _queries = queries;
        _reference = reference;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _logger = logger;
    }

    // -----------------------------------------------------------------
    // Categories
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(
        ServiceModule? module,
        CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.CategoryRead);
        return _queries.GetCategoriesAsync(module, ct);
    }

    /// <inheritdoc />
    public async Task<CategoryDto> CreateCategoryAsync(
        UpsertCategoryCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.CategoryManage);

        var code = Code(command.Code, command.Name);

        await ValidateCategoryAsync(command, code, null, ct).ConfigureAwait(false);

        var category = new Category
        {
            Code = code,
            Name = command.Name.Trim(),
            Description = Clean(command.Description),
            Module = command.Module,
            DefaultAssignmentGroupId = command.DefaultAssignmentGroupId,
            SortOrder = command.SortOrder,
            IsActive = command.IsActive
        };

        _admin.AddCategory(category);

        _audit.Record(
            AuditAction.Configuration,
            nameof(Category),
            category.Id.ToString(),
            category.Name,
            $"Created the {command.Module} category \"{category.Name}\".");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await LoadCategoryDtoAsync(category.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CategoryDto> UpdateCategoryAsync(
        Guid id,
        UpsertCategoryCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.CategoryManage);

        var category = await LoadCategoryAsync(id, ct).ConfigureAwait(false);

        if (command.RowVersion is { Length: > 0 })
        {
            _admin.SetExpectedVersion(category, command.RowVersion);
        }

        await ValidateCategoryAsync(command, category.Code, id, ct).ConfigureAwait(false);

        // The module is fixed. Records already classified here belong to the old module, and
        // moving the category would leave them classified under a taxonomy that no longer
        // claims them — visible in a list view, unreachable from any filter.
        if (category.Module != command.Module)
        {
            throw new DomainException(
                "category.module_immutable",
                "A category cannot be moved between modules. Create one in the other module instead.");
        }

        category.Name = command.Name.Trim();
        category.Description = Clean(command.Description);
        category.DefaultAssignmentGroupId = command.DefaultAssignmentGroupId;
        category.SortOrder = command.SortOrder;
        category.IsActive = command.IsActive;

        _audit.Record(
            AuditAction.Configuration,
            nameof(Category),
            category.Id.ToString(),
            category.Name,
            $"Updated the category \"{category.Name}\".");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await LoadCategoryDtoAsync(id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteCategoryAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.CategoryManage);

        var category = await LoadCategoryAsync(id, ct).ConfigureAwait(false);

        var records = await _admin.CountRecordsInCategoryAsync(id, ct).ConfigureAwait(false);

        if (records > 0)
        {
            // Deleting it would strip the classification from records that already have one, and
            // reporting for the period they were raised in would silently change. Deactivating
            // hides it from new records and leaves the history intact, which is what people
            // actually want when they say "remove this category".
            throw new DomainException(
                "category.in_use",
                $"{records} record{(records == 1 ? "" : "s")} classify here. "
                + "Deactivate the category instead — it will stop appearing on new records and "
                + "the existing ones keep their classification.");
        }

        _admin.RemoveCategory(category);

        _audit.Record(
            AuditAction.Configuration,
            nameof(Category),
            category.Id.ToString(),
            category.Name,
            $"Deleted the unused category \"{category.Name}\".");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("Category {CategoryId} deleted by {Actor}.", id, _currentUser.UserId);
    }

    /// <inheritdoc />
    public async Task<CategoryDto> AddSubcategoryAsync(
        Guid categoryId,
        UpsertSubcategoryCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.CategoryManage);

        var category = await LoadCategoryAsync(categoryId, ct).ConfigureAwait(false);

        var code = Code(command.Code, command.Name);

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            throw new ValidationException([new(nameof(command.Name), "A subcategory needs a name.")]);
        }

        if (await _admin.SubcategoryCodeExistsAsync(categoryId, code, null, ct).ConfigureAwait(false))
        {
            throw new ValidationException(
                [new(nameof(command.Code), "That code is already used in this category.")]);
        }

        _admin.AddSubcategory(new Subcategory
        {
            CategoryId = categoryId,
            Code = code,
            Name = command.Name.Trim(),
            Description = Clean(command.Description),
            DefaultAssignmentGroupId = command.DefaultAssignmentGroupId,
            SortOrder = command.SortOrder,
            IsActive = command.IsActive
        });

        _audit.Record(
            AuditAction.Configuration,
            nameof(Subcategory),
            categoryId.ToString(),
            category.Name,
            $"Added the subcategory \"{command.Name.Trim()}\" to \"{category.Name}\".");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await LoadCategoryDtoAsync(categoryId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CategoryDto> UpdateSubcategoryAsync(
        Guid id,
        UpsertSubcategoryCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.CategoryManage);

        var subcategory = await _admin.GetSubcategoryAsync(id, ct).ConfigureAwait(false)
                          ?? throw new EntityNotFoundException(nameof(Subcategory), id);

        var code = Code(command.Code, command.Name);

        if (await _admin.SubcategoryCodeExistsAsync(subcategory.CategoryId, code, id, ct).ConfigureAwait(false))
        {
            throw new ValidationException(
                [new(nameof(command.Code), "That code is already used in this category.")]);
        }

        subcategory.Code = code;
        subcategory.Name = command.Name.Trim();
        subcategory.Description = Clean(command.Description);
        subcategory.DefaultAssignmentGroupId = command.DefaultAssignmentGroupId;
        subcategory.SortOrder = command.SortOrder;
        subcategory.IsActive = command.IsActive;

        _audit.Record(
            AuditAction.Configuration,
            nameof(Subcategory),
            subcategory.Id.ToString(),
            subcategory.Name,
            $"Updated the subcategory \"{subcategory.Name}\".");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await LoadCategoryDtoAsync(subcategory.CategoryId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CategoryDto> DeleteSubcategoryAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.CategoryManage);

        var subcategory = await _admin.GetSubcategoryAsync(id, ct).ConfigureAwait(false)
                          ?? throw new EntityNotFoundException(nameof(Subcategory), id);

        var categoryId = subcategory.CategoryId;

        var records = await _admin.CountRecordsInSubcategoryAsync(id, ct).ConfigureAwait(false);

        if (records > 0)
        {
            throw new DomainException(
                "subcategory.in_use",
                $"{records} record{(records == 1 ? "" : "s")} classify here. Deactivate it instead.");
        }

        _admin.RemoveSubcategory(subcategory);

        _audit.Record(
            AuditAction.Configuration,
            nameof(Subcategory),
            subcategory.Id.ToString(),
            subcategory.Name,
            $"Deleted the unused subcategory \"{subcategory.Name}\".");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await LoadCategoryDtoAsync(categoryId, ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Groups
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public Task<IReadOnlyList<GroupAdminDto>> GetGroupsAsync(CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.GroupRead);
        return _queries.GetGroupsAsync(ct);
    }

    /// <inheritdoc />
    public async Task<GroupAdminDto> GetGroupAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.GroupRead);

        return await _queries.GetGroupAsync(id, ct).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(nameof(Group), id);
    }

    /// <inheritdoc />
    public async Task<GroupAdminDto> CreateGroupAsync(UpsertGroupCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.GroupManage);

        var code = Code(command.Code, command.Name);

        await ValidateGroupAsync(command, code, null, ct).ConfigureAwait(false);

        var group = new Group
        {
            Code = code,
            Name = command.Name.Trim(),
            Description = Clean(command.Description),
            Type = command.Type,
            Email = Clean(command.Email)?.ToLowerInvariant(),
            ManagerUserId = command.ManagerUserId,
            DefaultAssigneeUserId = command.DefaultAssigneeUserId,
            BusinessCalendarId = command.BusinessCalendarId,
            IsActive = command.IsActive
        };

        _admin.AddGroup(group);

        _audit.Record(
            AuditAction.Configuration,
            nameof(Group),
            group.Id.ToString(),
            group.Name,
            $"Created the {command.Type} group \"{group.Name}\".");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await GetGroupAsync(group.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<GroupAdminDto> UpdateGroupAsync(
        Guid id,
        UpsertGroupCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.GroupManage);

        var group = await LoadGroupAsync(id, ct).ConfigureAwait(false);

        if (command.RowVersion is { Length: > 0 })
        {
            _admin.SetExpectedVersion(group, command.RowVersion);
        }

        await ValidateGroupAsync(command, group.Code, id, ct).ConfigureAwait(false);

        group.Name = command.Name.Trim();
        group.Description = Clean(command.Description);
        group.Type = command.Type;
        group.Email = Clean(command.Email)?.ToLowerInvariant();
        group.ManagerUserId = command.ManagerUserId;
        group.DefaultAssigneeUserId = command.DefaultAssigneeUserId;
        group.BusinessCalendarId = command.BusinessCalendarId;
        group.IsActive = command.IsActive;

        _audit.Record(
            AuditAction.Configuration,
            nameof(Group),
            group.Id.ToString(),
            group.Name,
            $"Updated the group \"{group.Name}\".");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await GetGroupAsync(id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<GroupAdminDto> SetMemberAsync(
        Guid groupId,
        SetGroupMemberCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.GroupManage);

        var group = await LoadGroupAsync(groupId, ct).ConfigureAwait(false);

        // Tenant-filtered, so somebody from a neighbouring tenant reads as non-existent rather
        // than being quietly added to this tenant's queue.
        if (!await _reference.UserExistsAsync(command.UserId, ct).ConfigureAwait(false))
        {
            throw new EntityNotFoundException(nameof(User), command.UserId);
        }

        var existing = group.Members.FirstOrDefault(m => m.UserId == command.UserId);

        if (existing is not null)
        {
            // Already a member: this is a lead-flag change, not a duplicate membership.
            existing.IsLead = command.IsLead;
        }
        else
        {
            var member = new GroupMember
            {
                GroupId = groupId,
                UserId = command.UserId,
                IsLead = command.IsLead
            };

            group.Members.Add(member);
            _admin.AddMember(member);
        }

        _audit.Record(
            AuditAction.Configuration,
            nameof(Group),
            group.Id.ToString(),
            group.Name,
            $"Membership of \"{group.Name}\" changed.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await GetGroupAsync(groupId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<GroupAdminDto> RemoveMemberAsync(
        Guid groupId,
        Guid userId,
        CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.GroupManage);

        var group = await LoadGroupAsync(groupId, ct).ConfigureAwait(false);

        var member = group.Members.FirstOrDefault(m => m.UserId == userId);

        if (member is null)
        {
            return await GetGroupAsync(groupId, ct).ConfigureAwait(false);
        }

        // Removing somebody who is the group's default assignee would leave work routed to the
        // group falling to a person who is no longer in it.
        if (group.DefaultAssigneeUserId == userId)
        {
            throw new DomainException(
                "group.member_is_default_assignee",
                "This person receives work routed to the group but not to a named person. "
                + "Choose a different default assignee before removing them.");
        }

        group.Members.Remove(member);
        _admin.RemoveMember(member);

        _audit.Record(
            AuditAction.Configuration,
            nameof(Group),
            group.Id.ToString(),
            group.Name,
            $"Removed a member from \"{group.Name}\".");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await GetGroupAsync(groupId, ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private async Task<Category> LoadCategoryAsync(Guid id, CancellationToken ct)
        => await _admin.GetCategoryAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(nameof(Category), id);

    private async Task<Group> LoadGroupAsync(Guid id, CancellationToken ct)
        => await _admin.GetGroupAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(nameof(Group), id);

    private async Task<CategoryDto> LoadCategoryDtoAsync(Guid id, CancellationToken ct)
        => await _queries.GetCategoryAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(nameof(Category), id);

    private async Task ValidateCategoryAsync(
        UpsertCategoryCommand command,
        string code,
        Guid? exceptId,
        CancellationToken ct)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            failures.Add(new(nameof(command.Name), "A category needs a name."));
        }

        if (command.DefaultAssignmentGroupId is not null
            && !await _reference.GroupExistsAsync(command.DefaultAssignmentGroupId.Value, ct).ConfigureAwait(false))
        {
            failures.Add(new(nameof(command.DefaultAssignmentGroupId), "That group does not exist."));
        }

        if (failures.Count == 0
            && await _admin.CategoryCodeExistsAsync(code, command.Module, exceptId, ct).ConfigureAwait(false))
        {
            failures.Add(new(
                nameof(command.Code),
                $"A {command.Module} category with that code already exists."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }

    private async Task ValidateGroupAsync(
        UpsertGroupCommand command,
        string code,
        Guid? exceptId,
        CancellationToken ct)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            failures.Add(new(nameof(command.Name), "A group needs a name."));
        }

        foreach (var (userId, field) in new[]
                 {
                     (command.ManagerUserId, nameof(command.ManagerUserId)),
                     (command.DefaultAssigneeUserId, nameof(command.DefaultAssigneeUserId))
                 })
        {
            if (userId is not null && !await _reference.UserExistsAsync(userId.Value, ct).ConfigureAwait(false))
            {
                failures.Add(new(field, "That person does not exist or is no longer active."));
            }
        }

        if (failures.Count == 0
            && await _admin.GroupCodeExistsAsync(code, exceptId, ct).ConfigureAwait(false))
        {
            failures.Add(new(nameof(command.Code), "A group with that code already exists."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }

    /// <summary>An upper-case code, derived from the name when none was given.</summary>
    private static string Code(string? code, string name)
    {
        var source = string.IsNullOrWhiteSpace(code) ? name : code;

        var cleaned = new string(
            (source ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '_')
                .ToArray());

        while (cleaned.Contains("__", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        }

        return cleaned.Trim('_');
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
