using FluentValidation;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Security;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Common;
using NexaOps.Domain.Identity;

namespace NexaOps.Application.Administration;

/// <summary>Roles and what they grant.</summary>
public interface IRoleAdminService
{
    Task<IReadOnlyList<RoleDetailDto>> GetRolesAsync(CancellationToken ct = default);

    Task<RoleDetailDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>The permission catalogue, so the editor offers what the server will accept.</summary>
    IReadOnlyList<PermissionDto> GetPermissions();

    Task<RoleDetailDto> CreateAsync(UpsertRoleCommand command, CancellationToken ct = default);

    Task<RoleDetailDto> UpdateAsync(Guid id, UpsertRoleCommand command, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class RoleAdminService : IRoleAdminService
{
    private const string EntityType = nameof(Role);

    private readonly IAdministrationRepository _admin;
    private readonly IAdministrationQueryService _queries;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<RoleAdminService> _logger;

    public RoleAdminService(
        IAdministrationRepository admin,
        IAdministrationQueryService queries,
        IAuditService audit,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ILogger<RoleAdminService> logger)
    {
        _admin = admin;
        _queries = queries;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RoleDetailDto>> GetRolesAsync(CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.RoleRead);
        return _queries.GetRolesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<RoleDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.RoleRead);

        return await _queries.GetRoleAsync(id, ct).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(EntityType, id);
    }

    /// <inheritdoc />
    public IReadOnlyList<PermissionDto> GetPermissions()
    {
        _currentUser.DemandPermission(Permissions.RoleRead);

        // Platform permissions are withheld rather than shown and disabled. They grant across
        // every tenant, and a tenant administrator cannot hold or assign them, so listing them
        // would only advertise a boundary they are on the wrong side of.
        return
        [
            .. Permissions.Catalogue
                .Where(p => !p.Code.StartsWith("platform.", StringComparison.Ordinal))
                .Select(p => new PermissionDto(p.Code, p.Module, p.Name, p.Description))
        ];
    }

    /// <inheritdoc />
    public async Task<RoleDetailDto> CreateAsync(UpsertRoleCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.RoleManage);

        var code = Slug(command.Code, command.Name);

        await ValidateAsync(command, code, null, ct).ConfigureAwait(false);

        var role = new Role
        {
            Code = code,
            Name = command.Name.Trim(),
            Description = Clean(command.Description),

            // A role created here is a tenant role. Neither flag can be set through this API:
            // IsSystem would make it undeletable, and IsPlatformScoped would grant across every
            // tenant, which is not something a tenant may award itself.
            IsSystem = false,
            IsPlatformScoped = false
        };

        foreach (var permission in Sanitise(command.Permissions))
        {
            role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionCode = permission });
        }

        _admin.AddRole(role);

        _audit.Record(
            AuditAction.PermissionChange,
            EntityType,
            role.Id.ToString(),
            role.Name,
            $"Created the role \"{role.Name}\" with {role.RolePermissions.Count} permission(s).");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("Role {RoleId} created by {Actor}.", role.Id, _currentUser.UserId);

        return await GetAsync(role.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RoleDetailDto> UpdateAsync(Guid id, UpsertRoleCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.RoleManage);

        var role = await LoadAsync(id, ct).ConfigureAwait(false);

        if (command.RowVersion is { Length: > 0 })
        {
            _admin.SetExpectedVersion(role, command.RowVersion);
        }

        // System roles are what a tenant is provisioned with and what the seeder maintains.
        // Editing one would be silently reverted on the next provisioning run, so it is refused
        // rather than accepted and lost. Copying it into a tenant role is the supported path.
        if (role.IsSystem)
        {
            throw new DomainException(
                "role.system_immutable",
                "A built-in role cannot be edited. Create a role of your own instead.");
        }

        await ValidateAsync(command, role.Code, id, ct).ConfigureAwait(false);

        role.Name = command.Name.Trim();
        role.Description = Clean(command.Description);

        var wanted = Sanitise(command.Permissions).ToHashSet(StringComparer.Ordinal);
        var held = role.RolePermissions.ToList();

        foreach (var permission in held.Where(p => !wanted.Contains(p.PermissionCode)))
        {
            _admin.RemoveRolePermission(permission);
            role.RolePermissions.Remove(permission);
        }

        foreach (var permission in wanted.Where(p => held.All(h => h.PermissionCode != p)))
        {
            role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionCode = permission });
        }

        // Note what is deliberately absent: the security stamps of everyone holding this role are
        // not rotated. Doing so would sign out an entire department because somebody corrected a
        // description, and the permission check reads the role's grants on each request anyway —
        // it is role *assignment* that is baked into a token, not the role's contents.
        _audit.Record(
            AuditAction.PermissionChange,
            EntityType,
            role.Id.ToString(),
            role.Name,
            $"Updated the role \"{role.Name}\"; it now grants {wanted.Count} permission(s).");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await GetAsync(id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.RoleManage);

        var role = await LoadAsync(id, ct).ConfigureAwait(false);

        if (role.IsSystem)
        {
            throw new DomainException(
                "role.system_immutable",
                "A built-in role cannot be deleted.");
        }

        var users = await _admin.CountUsersInRoleAsync(id, ct).ConfigureAwait(false);

        if (users > 0)
        {
            // Deleting it would silently strip permissions from everybody holding it, and the
            // audit trail would record a role deletion rather than the access change that
            // actually happened to each of them.
            throw new DomainException(
                "role.in_use",
                $"{users} {(users == 1 ? "person holds" : "people hold")} this role. "
                + "Move them to another role first.");
        }

        // The grants are removed explicitly rather than left to the database cascade. The role
        // was loaded with them, so the change tracker holds children whose required parent is
        // about to disappear, and EF refuses to save that as an orphan regardless of what the
        // foreign key says it would do. Being explicit is also the more honest record: the audit
        // entry below can state how much access this actually removed.
        var granted = role.RolePermissions.Count;

        foreach (var permission in role.RolePermissions.ToList())
        {
            _admin.RemoveRolePermission(permission);
        }

        role.RolePermissions.Clear();

        _admin.RemoveRole(role);

        _audit.Record(
            AuditAction.PermissionChange,
            EntityType,
            role.Id.ToString(),
            role.Name,
            $"Deleted the role \"{role.Name}\", which granted {granted} permission(s).");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("Role {RoleId} deleted by {Actor}.", id, _currentUser.UserId);
    }

    private async Task<Role> LoadAsync(Guid id, CancellationToken ct)
        => await _admin.GetRoleAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(EntityType, id);

    /// <summary>
    /// Keeps only codes the server actually recognises.
    /// <para>
    /// An unknown code stored on a role is a grant that never matches anything — it looks like
    /// access has been given and none has. Silently dropping it would be as bad; the validator
    /// refuses the request instead, and this is the belt to that pair of braces.
    /// </para>
    /// </summary>
    private static IEnumerable<string> Sanitise(IEnumerable<string> permissions)
        => permissions
            .Select(p => p?.Trim() ?? string.Empty)
            .Where(p => p.Length > 0 && Permissions.IsKnown(p))
            .Where(p => !p.StartsWith("platform.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal);

    private async Task ValidateAsync(
        UpsertRoleCommand command,
        string code,
        Guid? exceptId,
        CancellationToken ct)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            failures.Add(new(nameof(command.Name), "A role needs a name."));
        }

        var unknown = command.Permissions
            .Select(p => p?.Trim() ?? string.Empty)
            .Where(p => p.Length > 0 && !Permissions.IsKnown(p))
            .ToList();

        if (unknown.Count > 0)
        {
            failures.Add(new(
                nameof(command.Permissions),
                $"These are not permissions this system has: {string.Join(", ", unknown)}."));
        }

        if (command.Permissions.Any(p => p?.StartsWith("platform.", StringComparison.Ordinal) == true))
        {
            failures.Add(new(
                nameof(command.Permissions),
                "Platform permissions cannot be granted from within a tenant."));
        }

        if (failures.Count == 0
            && await _admin.RoleCodeExistsAsync(code, exceptId, ct).ConfigureAwait(false))
        {
            failures.Add(new(nameof(command.Code), "A role with that code already exists."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }

    /// <summary>
    /// A stable code, derived from the name when none was supplied.
    /// <para>
    /// The code is what appears in audit entries and in support conversations, so it is
    /// lower-cased and hyphenated rather than left as free text.
    /// </para>
    /// </summary>
    private static string Slug(string? code, string name)
    {
        var source = string.IsNullOrWhiteSpace(code) ? name : code;

        var slug = new string(
            (source ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '-')
                .ToArray());

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
