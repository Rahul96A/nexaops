using FluentValidation;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Security;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Common;
using NexaOps.Domain.Identity;

namespace NexaOps.Application.Platform;

/// <summary>
/// Platform administration: the operator's view of their customers.
/// <para>
/// Every method here demands a <c>platform.*</c> permission, which only a platform-scoped role
/// carries and which a tenant administrator cannot grant to anybody — see
/// <c>UserAdminService.SetRolesAsync</c>. That refusal and these demands are the two halves of
/// the same boundary, and neither is sufficient alone.
/// </para>
/// </summary>
public interface ITenantOnboardingService
{
    Task<PagedResult<TenantSummaryDto>> SearchAsync(TenantQuery query, CancellationToken ct = default);

    Task<TenantDetailDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates a customer tenant and its first administrator.</summary>
    Task<TenantOnboardingResult> OnboardAsync(CreateTenantCommand command, CancellationToken ct = default);

    Task<TenantDetailDto> UpdateAsync(Guid id, UpdateTenantCommand command, CancellationToken ct = default);

    Task<TenantDetailDto> SetStatusAsync(Guid id, SetTenantStatusCommand command, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class TenantOnboardingService : ITenantOnboardingService
{
    private const string EntityType = nameof(Tenant);

    private readonly IPlatformRepository _platform;
    private readonly ITenantProvisioner _provisioner;
    private readonly ICredentialService _credentials;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenant;
    private readonly IDateTimeProvider _clock;
    private readonly IValidator<CreateTenantCommand> _createValidator;
    private readonly IValidator<UpdateTenantCommand> _updateValidator;
    private readonly ILogger<TenantOnboardingService> _logger;

    public TenantOnboardingService(
        IPlatformRepository platform,
        ITenantProvisioner provisioner,
        ICredentialService credentials,
        IAuditService audit,
        ICurrentUser currentUser,
        ITenantContext tenant,
        IDateTimeProvider clock,
        IValidator<CreateTenantCommand> createValidator,
        IValidator<UpdateTenantCommand> updateValidator,
        ILogger<TenantOnboardingService> logger)
    {
        _platform = platform;
        _provisioner = provisioner;
        _credentials = credentials;
        _audit = audit;
        _currentUser = currentUser;
        _tenant = tenant;
        _clock = clock;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PagedResult<TenantSummaryDto>> SearchAsync(TenantQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        _currentUser.DemandPermission(Permissions.PlatformTenantRead);

        return await _platform.SearchTenantsAsync(query, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TenantDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        _currentUser.DemandPermission(Permissions.PlatformTenantRead);

        return await _platform.GetTenantAsync(id, ct).ConfigureAwait(false)
               ?? throw new EntityNotFoundException(EntityType, id);
    }

    /// <inheritdoc />
    public async Task<TenantOnboardingResult> OnboardAsync(
        CreateTenantCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.PlatformTenantManage);

        await _createValidator.ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        // Onboarding mints a local password for the first administrator. Under federated
        // authentication it cannot: NexaOps issues no credentials there, and first-sign-in user
        // provisioning from Entra is not built yet, so the account would exist with no way in.
        // Refusing is the honest answer — the alternative is an onboarding that reports success
        // and hands over a tenant nobody can enter.
        if (!_credentials.IssuesLocalCredentials)
        {
            throw new DomainException(
                "platform.federated_onboarding_unsupported",
                "This deployment authenticates through an external identity provider, and creating "
                + "the first administrator requires issuing a local password. Onboard the tenant in a "
                + "deployment configured for local authentication, or build Entra first-sign-in "
                + "provisioning first.");
        }

        var code = Normalise(command.Code);
        var email = NormaliseEmail(command.AdministratorEmail);

        if (await _platform.TenantCodeExistsAsync(code, ct).ConfigureAwait(false))
        {
            throw new DomainException(
                "platform.tenant_code_taken",
                $"The tenant code '{code}' is already in use.");
        }

        // Provisioning creates the tenant and everything it needs to function: roles and their
        // permission grants, the priority matrix, calendars, SLA definitions and policies,
        // number sequences and the change advisory board.
        var tenant = await _provisioner
            .ProvisionAsync(
                new TenantProvisioningRequest
                {
                    Code = code,
                    Name = command.Name.Trim(),
                    LegalName = string.IsNullOrWhiteSpace(command.LegalName) ? null : command.LegalName.Trim(),
                    PrimaryDomain = string.IsNullOrWhiteSpace(command.PrimaryDomain)
                        ? null
                        : command.PrimaryDomain.Trim().ToLowerInvariant(),
                    Status = command.Status
                },
                ct)
            .ConfigureAwait(false);

        // The tenant now exists but nobody can sign in to it. The rest of this method is what
        // turns a provisioned tenant into an onboarded customer.
        var administratorRole = await _platform
            .GetTenantRoleByCodeAsync(tenant.Id, SystemRoles.TenantAdministrator, ct)
            .ConfigureAwait(false);

        if (administratorRole is null)
        {
            // Provisioning seeds this role, so its absence means provisioning did not complete.
            // Failing here rather than creating a user with no permissions makes that visible.
            throw new DomainException(
                "platform.provisioning_incomplete",
                "The tenant was created but its administrator role is missing. Provisioning did not complete.");
        }

        if (administratorRole.IsPlatformScoped)
        {
            // Defensive: the tenant administrator role must never be platform-scoped. If a
            // future edit to SystemRoles made it so, onboarding would hand every customer
            // control of the platform, and it would look exactly like a successful onboarding.
            throw new DomainException(
                "platform.role_scope_violation",
                "The tenant administrator role is platform-scoped, which would grant this customer control of the platform.");
        }

        if (await _platform.UserEmailExistsInTenantAsync(tenant.Id, email, ct).ConfigureAwait(false))
        {
            throw new DomainException(
                "platform.administrator_exists",
                $"{email} is already a user in this tenant.");
        }

        var firstName = command.AdministratorFirstName.Trim();
        var lastName = command.AdministratorLastName.Trim();

        var administrator = new User
        {
            TenantId = tenant.Id,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            DisplayName = $"{firstName} {lastName}".Trim(),
            JobTitle = string.IsNullOrWhiteSpace(command.AdministratorJobTitle)
                ? null
                : command.AdministratorJobTitle.Trim(),
            Status = UserStatus.Active,
            TimeZoneId = tenant.TimeZoneId,
            Locale = tenant.Locale,
            CreatedAt = _clock.UtcNow
        };

        var temporaryPassword = _credentials.GenerateTemporaryPassword();
        administrator.PasswordHash = _credentials.Hash(administrator, temporaryPassword);
        administrator.PasswordChangedAt = _clock.UtcNow;

        // The operator knows this password, so the customer must replace it before doing
        // anything. Without this the platform operator retains a working credential inside a
        // customer's tenant indefinitely.
        administrator.MustChangePassword = true;

        _platform.AddUserForTenant(
            administrator,
            new UserRole
            {
                TenantId = tenant.Id,
                UserId = administrator.Id,
                RoleId = administratorRole.Id,
                CreatedAt = _clock.UtcNow
            });

        await _platform.SaveCrossTenantAsync(ct).ConfigureAwait(false);

        // Written into the new tenant's own trail rather than the operator's: this is the first
        // entry in the customer's history and it names who created them. The password is not in
        // it, and is not in the log below either.
        await _audit.RecordImmediateAsync(
            AuditAction.Create,
            EntityType,
            tenant.Id.ToString(),
            tenant.Code,
            $"Tenant '{tenant.Name}' was created by the platform operator, with {email} as its first administrator.",
            AuditSource.Api,
            AuditOutcome.Success,
            tenantIdOverride: tenant.Id,
            actorUserIdOverride: _currentUser.UserIdOrNull,
            cancellationToken: ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Tenant {TenantCode} ({TenantId}) onboarded by platform user {Actor} with administrator {AdministratorId}.",
            tenant.Code, tenant.Id, _currentUser.UserId, administrator.Id);

        var detail = await _platform.GetTenantAsync(tenant.Id, ct).ConfigureAwait(false)
                     ?? throw new EntityNotFoundException(EntityType, tenant.Id);

        return new TenantOnboardingResult(detail, administrator.Id, email, temporaryPassword);
    }

    /// <inheritdoc />
    public async Task<TenantDetailDto> UpdateAsync(
        Guid id,
        UpdateTenantCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.PlatformTenantManage);

        await _updateValidator.ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        var tenant = await _platform.GetTenantEntityAsync(id, ct).ConfigureAwait(false)
                     ?? throw new EntityNotFoundException(EntityType, id);

        // Code is absent from the command on purpose. It is embedded in the sign-in flow, in
        // support conversations and in whatever the customer has bookmarked; changing it would
        // silently break tenant disambiguation for every user whose email exists twice.
        tenant.Name = command.Name.Trim();
        tenant.LegalName = string.IsNullOrWhiteSpace(command.LegalName) ? null : command.LegalName.Trim();
        tenant.PrimaryDomain = string.IsNullOrWhiteSpace(command.PrimaryDomain)
            ? null
            : command.PrimaryDomain.Trim().ToLowerInvariant();
        tenant.EntraTenantId = string.IsNullOrWhiteSpace(command.EntraTenantId)
            ? null
            : command.EntraTenantId.Trim();
        tenant.RecordRetentionDays = command.RecordRetentionDays;
        tenant.AuditRetentionDays = command.AuditRetentionDays;
        tenant.UpdatedAt = _clock.UtcNow;
        tenant.UpdatedBy = _currentUser.UserIdOrNull;

        await _platform.SaveCrossTenantAsync(ct).ConfigureAwait(false);

        await _audit.RecordImmediateAsync(
            AuditAction.Configuration,
            EntityType,
            tenant.Id.ToString(),
            tenant.Code,
            "Tenant settings were changed by the platform operator.",
            AuditSource.Api,
            AuditOutcome.Success,
            tenantIdOverride: tenant.Id,
            actorUserIdOverride: _currentUser.UserIdOrNull,
            cancellationToken: ct).ConfigureAwait(false);

        return await GetAsync(tenant.Id, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TenantDetailDto> SetStatusAsync(
        Guid id,
        SetTenantStatusCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _currentUser.DemandPermission(Permissions.PlatformTenantManage);

        if (!Enum.IsDefined(command.Status))
        {
            throw new DomainException("platform.unknown_status", "That is not a tenant status.");
        }

        // Suspending the tenant you are signed in to locks you out of the tool you would use to
        // undo it, and there is no way back in from outside the product.
        if (_tenant.HasTenant && _tenant.TenantId == id
            && command.Status is TenantStatus.Suspended or TenantStatus.Closed)
        {
            throw new DomainException(
                "platform.cannot_suspend_own_tenant",
                "You cannot suspend or close the tenant you are signed in to.");
        }

        var tenant = await _platform.GetTenantEntityAsync(id, ct).ConfigureAwait(false)
                     ?? throw new EntityNotFoundException(EntityType, id);

        var previous = tenant.Status;

        if (previous == command.Status)
        {
            return await GetAsync(id, ct).ConfigureAwait(false);
        }

        var blocked = command.Status is TenantStatus.Suspended or TenantStatus.Closed;

        if (blocked && string.IsNullOrWhiteSpace(command.Reason))
        {
            // Suspension is a commercial act with an explanation behind it. Requiring the reason
            // means the audit trail answers "why can nobody sign in" without a support call.
            throw new DomainException(
                "platform.reason_required",
                "Suspending or closing a tenant requires a reason.");
        }

        tenant.Status = command.Status;
        tenant.UpdatedAt = _clock.UtcNow;
        tenant.UpdatedBy = _currentUser.UserIdOrNull;

        await _platform.SaveCrossTenantAsync(ct).ConfigureAwait(false);

        var revoked = 0;

        if (blocked)
        {
            // After the status change commits, not before. Sign-in and refresh both refuse a
            // blocked tenant on their own, so this revocation is cleanup rather than the control
            // itself — and doing it first would mean a failed save had signed out every user of
            // a tenant that is still active.
            //
            // What it buys: without it the rows sit there looking live until they expire.
            // Access already granted still runs until the current access token expires, which
            // is the ceiling on how quickly a suspension can take effect.
            revoked = await _platform
                .RevokeTenantRefreshTokensAsync(tenant.Id, $"Tenant {command.Status}.", ct)
                .ConfigureAwait(false);
        }

        await _audit.RecordImmediateAsync(
            AuditAction.StatusChange,
            EntityType,
            tenant.Id.ToString(),
            tenant.Code,
            blocked
                ? $"Tenant moved from {previous} to {command.Status} by the platform operator. "
                  + $"Reason: {command.Reason!.Trim()}. {revoked} active session(s) were revoked."
                : $"Tenant moved from {previous} to {command.Status} by the platform operator.",
            AuditSource.Api,
            AuditOutcome.Success,
            tenantIdOverride: tenant.Id,
            actorUserIdOverride: _currentUser.UserIdOrNull,
            cancellationToken: ct).ConfigureAwait(false);

        _logger.LogWarning(
            "Tenant {TenantCode} moved from {Previous} to {Status} by {Actor}; {Revoked} sessions revoked.",
            tenant.Code, previous, command.Status, _currentUser.UserId, revoked);

        return await GetAsync(id, ct).ConfigureAwait(false);
    }

    private static string Normalise(string code) => code.Trim().ToLowerInvariant();

    private static string NormaliseEmail(string email) => email.Trim().ToLowerInvariant();
}
