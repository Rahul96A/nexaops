using FluentValidation;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Security;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Common;
using NexaOps.Domain.Identity;

namespace NexaOps.Application.Administration;

/// <summary>Directory administration: who exists, what they may do, and whether they may sign in.</summary>
public interface IUserAdminService
{
    Task<PagedResult<UserListItemDto>> SearchAsync(UserSearchQuery query, CancellationToken ct = default);

    Task<UserAdminDto> GetAsync(Guid id, CancellationToken ct = default);

    Task<CreatedUserDto> CreateAsync(UpsertUserCommand command, CancellationToken ct = default);

    Task<UserAdminDto> UpdateAsync(Guid id, UpsertUserCommand command, CancellationToken ct = default);

    /// <summary>Replaces the user's roles with exactly this set.</summary>
    Task<UserAdminDto> SetRolesAsync(Guid id, SetUserRolesCommand command, CancellationToken ct = default);

    /// <summary>Enables, disables or suspends an account.</summary>
    Task<UserAdminDto> SetStatusAsync(Guid id, SetUserStatusCommand command, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class UserAdminService : IUserAdminService
{
    private const string EntityType = nameof(User);

    private readonly IAdministrationRepository _admin;
    private readonly IAdministrationQueryService _queries;
    private readonly ICredentialService _credentials;
    private readonly ISecurityStampCache _stampCache;
    private readonly IAuditService _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<UserAdminService> _logger;

    public UserAdminService(
        IAdministrationRepository admin,
        IAdministrationQueryService queries,
        ICredentialService credentials,
        ISecurityStampCache stampCache,
        IAuditService audit,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<UserAdminService> logger)
    {
        _admin = admin;
        _queries = queries;
        _credentials = credentials;
        _stampCache = stampCache;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<PagedResult<UserListItemDto>> SearchAsync(UserSearchQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        _currentUser.DemandPermission(Permissions.UserRead);

        return _queries.SearchUsersAsync(query, ct);
    }

    /// <inheritdoc />
    public async Task<UserAdminDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.UserRead);

        return await _queries.GetUserAsync(id, ct).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(EntityType, id);
    }

    /// <inheritdoc />
    public async Task<CreatedUserDto> CreateAsync(UpsertUserCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.UserManage);

        var email = Normalise(command.Email);

        await ValidateAsync(command, email, null, ct).ConfigureAwait(false);

        var user = new User
        {
            Email = email,
            FirstName = command.FirstName.Trim(),
            LastName = command.LastName.Trim(),
            DisplayName = $"{command.FirstName.Trim()} {command.LastName.Trim()}".Trim(),
            IsServiceAccount = command.IsServiceAccount,
            Status = UserStatus.Active
        };

        Apply(command, user);

        string? temporaryPassword = null;

        // Under federated authentication NexaOps issues no credentials: the account exists to be
        // matched to an external identity on first sign-in. Minting a local password there would
        // create a second, unmanaged way in that no directory administrator knows about.
        if (_credentials.IssuesLocalCredentials && !command.IsServiceAccount)
        {
            temporaryPassword = _credentials.GenerateTemporaryPassword();
            user.PasswordHash = _credentials.Hash(user, temporaryPassword);
            user.MustChangePassword = true;
            user.PasswordChangedAt = _clock.UtcNow;
        }

        _admin.AddUser(user);

        // The audit records that an account was created and by whom. It does not record the
        // password, which is returned to the caller once and never persisted in the clear.
        _audit.Record(
            AuditAction.Create,
            EntityType,
            user.Id.ToString(),
            user.Email,
            $"Created the account {user.Email}.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("User {UserId} created by {Actor}.", user.Id, _currentUser.UserId);

        var created = await GetAsync(user.Id, ct).ConfigureAwait(false);

        return new CreatedUserDto(created, temporaryPassword);
    }

    /// <inheritdoc />
    public async Task<UserAdminDto> UpdateAsync(Guid id, UpsertUserCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.UserManage);

        var user = await LoadAsync(id, ct).ConfigureAwait(false);

        if (command.RowVersion is { Length: > 0 })
        {
            _admin.SetExpectedVersion(user, command.RowVersion);
        }

        var email = Normalise(command.Email);

        await ValidateAsync(command, email, id, ct).ConfigureAwait(false);

        // A manager cycle would make the approval engine's manager resolution loop forever.
        if (command.ManagerId == id)
        {
            throw new DomainException(
                "user.self_manager",
                "Somebody cannot be their own manager.");
        }

        var emailChanged = !string.Equals(user.Email, email, StringComparison.Ordinal);

        user.Email = email;
        user.FirstName = command.FirstName.Trim();
        user.LastName = command.LastName.Trim();
        user.DisplayName = $"{user.FirstName} {user.LastName}".Trim();

        Apply(command, user);

        if (emailChanged)
        {
            // The email is the sign-in identifier, so changing it changes how the account
            // authenticates. Existing tokens are cut immediately rather than at expiry.
            await RotateSecurityStampAsync(user, "email changed", ct).ConfigureAwait(false);
        }

        _audit.Record(
            AuditAction.Update,
            EntityType,
            user.Id.ToString(),
            user.Email,
            emailChanged ? $"Updated {user.Email}, including the sign-in address." : $"Updated {user.Email}.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return await GetAsync(id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<UserAdminDto> SetRolesAsync(
        Guid id,
        SetUserRolesCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Deliberately the role permission rather than the user one: granting somebody a role is
        // granting its permissions, so it belongs with whoever is trusted to define them, not
        // with whoever maintains job titles and phone numbers.
        _currentUser.DemandPermission(Permissions.RoleManage);

        var user = await LoadAsync(id, ct).ConfigureAwait(false);

        var requested = command.RoleIds.Distinct().ToList();
        var roles = await _admin.GetRolesAsync(requested, ct).ConfigureAwait(false);

        if (roles.Count != requested.Count)
        {
            // Tenant-filtered, so a role from a neighbouring tenant simply is not found. Saying
            // "one of these roles does not exist" rather than which one avoids confirming
            // whether a given identifier is real somewhere else.
            throw new DomainException(
                "user.unknown_role",
                "One or more of those roles does not exist.");
        }

        // A platform-scoped role grants permissions across every tenant. A tenant administrator
        // holding role.manage must not be able to assign one — that is the boundary between
        // administering a tenant and administering the platform.
        if (roles.Any(r => r.IsPlatformScoped))
        {
            throw new DomainException(
                "user.platform_role",
                "A platform role cannot be assigned from within a tenant.");
        }

        var current = await _admin.GetUserRolesAsync(id, ct).ConfigureAwait(false);

        var toRemove = current.Where(ur => !requested.Contains(ur.RoleId)).ToList();
        var toAdd = requested.Where(roleId => current.All(ur => ur.RoleId != roleId)).ToList();

        if (toRemove.Count == 0 && toAdd.Count == 0)
        {
            return await GetAsync(id, ct).ConfigureAwait(false);
        }

        foreach (var assignment in toRemove)
        {
            _admin.RemoveUserRole(assignment);
        }

        foreach (var roleId in toAdd)
        {
            _admin.AddUserRole(new UserRole { UserId = id, RoleId = roleId });
        }

        // The whole point of the stamp. Without this, somebody whose access was just revoked
        // keeps it until their access token expires — which is exactly the window that matters
        // when a role is removed because somebody should no longer have it.
        await RotateSecurityStampAsync(user, "roles changed", ct).ConfigureAwait(false);

        _audit.Record(
            AuditAction.PermissionChange,
            EntityType,
            user.Id.ToString(),
            user.Email,
            $"Roles changed for {user.Email}: {toAdd.Count} granted, {toRemove.Count} revoked.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Roles changed for {UserId} by {Actor}: +{Added} -{Removed}.",
            id, _currentUser.UserId, toAdd.Count, toRemove.Count);

        return await GetAsync(id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<UserAdminDto> SetStatusAsync(
        Guid id,
        SetUserStatusCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.UserManage);

        var user = await LoadAsync(id, ct).ConfigureAwait(false);

        // Locking yourself out is recoverable only by somebody else, and in a tenant with one
        // administrator there may be nobody else. Refused rather than warned about.
        if (id == _currentUser.UserId && command.Status != UserStatus.Active)
        {
            throw new DomainException(
                "user.cannot_disable_self",
                "You cannot disable your own account.");
        }

        if (user.Status == command.Status)
        {
            return await GetAsync(id, ct).ConfigureAwait(false);
        }

        user.Status = command.Status;

        if (command.Status != UserStatus.Active)
        {
            // Disabling an account has to end its sessions now. An account disabled during an
            // incident that keeps working for another hour is not disabled.
            await RotateSecurityStampAsync(user, "account disabled", ct).ConfigureAwait(false);
        }

        _audit.Record(
            AuditAction.Update,
            EntityType,
            user.Id.ToString(),
            user.Email,
            $"{user.Email} set to {command.Status}.");

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("User {UserId} set to {Status} by {Actor}.", id, command.Status, _currentUser.UserId);

        return await GetAsync(id, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Invalidates every access token the user currently holds.
    /// <para>
    /// Each token carries the stamp it was issued under and the API compares it on every
    /// request, so changing it here takes effect on the user's very next call rather than when
    /// the token would have expired.
    /// </para>
    /// <para>
    /// The cached copy is evicted in the same breath. The API caches a verified stamp for a
    /// minute to avoid a database read per request; without this eviction it would keep
    /// comparing against the old value for that minute, and a revocation would silently take up
    /// to sixty seconds — which is not what "revoked" means to whoever pressed the button.
    /// </para>
    /// </summary>
    private async Task RotateSecurityStampAsync(User user, string reason, CancellationToken ct)
    {
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        await _stampCache.InvalidateAsync(user.Id, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Security stamp rotated for {UserId} because {Reason}.", user.Id, reason);
    }

    private async Task<User> LoadAsync(Guid id, CancellationToken ct)
        => await _admin.GetUserAsync(id, ct).ConfigureAwait(false)
           ?? throw new EntityNotFoundException(EntityType, id);

    private static void Apply(UpsertUserCommand command, User user)
    {
        user.PhoneNumber = Clean(command.PhoneNumber);
        user.EmployeeId = Clean(command.EmployeeId);
        user.JobTitle = Clean(command.JobTitle);
        user.OrganizationId = command.OrganizationId;
        user.DepartmentId = command.DepartmentId;
        user.ManagerId = command.ManagerId;
        user.Location = Clean(command.Location);
        user.TimeZoneId = Clean(command.TimeZoneId);
        user.Locale = Clean(command.Locale);
    }

    private async Task ValidateAsync(
        UpsertUserCommand command,
        string email,
        Guid? exceptId,
        CancellationToken ct)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
        {
            failures.Add(new(nameof(command.Email), "A valid email address is required."));
        }

        if (string.IsNullOrWhiteSpace(command.FirstName))
        {
            failures.Add(new(nameof(command.FirstName), "A first name is required."));
        }

        if (string.IsNullOrWhiteSpace(command.LastName))
        {
            failures.Add(new(nameof(command.LastName), "A last name is required."));
        }

        if (failures.Count == 0
            && await _admin.EmailExistsAsync(email, exceptId, ct).ConfigureAwait(false))
        {
            failures.Add(new(
                nameof(command.Email),
                "Somebody in this tenant already uses that email address."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }

    /// <summary>Lower-cased and trimmed, because it is the sign-in identifier.</summary>
    private static string Normalise(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
