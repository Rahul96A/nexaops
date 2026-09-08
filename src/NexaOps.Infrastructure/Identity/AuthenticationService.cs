using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Identity;
using NexaOps.Application.Security;
using NexaOps.Domain.Auditing;
using NexaOps.Domain.Identity;
using NexaOps.Infrastructure.Persistence;

namespace NexaOps.Infrastructure.Identity;

/// <inheritdoc />
public sealed class AuthenticationService : IAuthenticationService
{
    private readonly NexaOpsDbContext _context;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly ISecurityStampCache _stampCache;
    private readonly AuthOptions _options;
    private readonly ITenantContextSetter _tenantSetter;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _currentUser;
    private readonly ICorrelationContext _correlation;
    private readonly IAuditService _audit;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        NexaOpsDbContext context,
        IPasswordHasher<User> passwordHasher,
        ISecurityStampCache stampCache,
        IOptions<AuthOptions> options,
        ITenantContextSetter tenantSetter,
        ITenantContext tenant,
        ICurrentUser currentUser,
        ICorrelationContext correlation,
        IAuditService audit,
        IDateTimeProvider clock,
        ILogger<AuthenticationService> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _stampCache = stampCache;
        _options = options.Value;
        _tenantSetter = tenantSetter;
        _tenant = tenant;
        _currentUser = currentUser;
        _correlation = correlation;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AuthenticationResult> SignInAsync(
        SignInRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = request.Email.Trim().ToLowerInvariant();
        var now = _clock.UtcNow;

        // Sign-in happens before a tenant scope exists, so the lookup deliberately crosses the
        // tenant filter. It is constrained to an exact email match and nothing is returned to
        // the caller until a password has been verified.
        using var suppression = _context.SuppressTenantFilter();

        var candidates = await _context.Users
            .Include(u => u.UserRoles).ThenInclude(r => r.Role)
            .Where(u => u.Email == email && !u.IsArchived && !u.IsServiceAccount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.TenantCode))
        {
            var code = request.TenantCode.Trim().ToLowerInvariant();
            var tenantIds = await LoadTenantIdsByCodeAsync(code, cancellationToken).ConfigureAwait(false);
            candidates = candidates.Where(u => tenantIds.Contains(u.TenantId)).ToList();
        }

        if (candidates.Count == 0)
        {
            await RecordFailureAsync(email, "unknown_account", null, cancellationToken).ConfigureAwait(false);
            throw new AuthenticationFailedException("unknown_account");
        }

        if (candidates.Count > 1)
        {
            // The same person can genuinely exist in several tenants. Rather than guessing, ask.
            // This reveals only tenants that share this exact email, and only after the caller
            // has demonstrated knowledge of the address.
            var choices = await LoadTenantChoicesAsync(
                    candidates.Select(c => c.TenantId).Distinct().ToList(),
                    cancellationToken)
                .ConfigureAwait(false);

            throw new TenantSelectionRequiredException(choices);
        }

        var user = candidates[0];

        if (user.LockoutEndsAt is not null && user.LockoutEndsAt > now)
        {
            await RecordFailureAsync(email, "account_locked", user, cancellationToken).ConfigureAwait(false);
            throw new AuthenticationFailedException("account_locked");
        }

        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            // A federated-only account has no local password. Saying so would confirm the
            // account exists, so the caller sees the same generic failure as anyone else.
            await RecordFailureAsync(email, "no_local_credential", user, cancellationToken).ConfigureAwait(false);
            throw new AuthenticationFailedException("no_local_credential");
        }

        var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            user.FailedLoginAttempts++;

            if (user.FailedLoginAttempts >= _options.MaxFailedLoginAttempts)
            {
                user.LockoutEndsAt = now.AddMinutes(_options.LockoutMinutes);
                user.FailedLoginAttempts = 0;

                _logger.LogWarning(
                    "Account {UserId} locked after repeated failed sign-in attempts from {Ip}.",
                    user.Id, _correlation.IpAddress);
            }

            await SaveIgnoringTenantAsync(cancellationToken).ConfigureAwait(false);
            await RecordFailureAsync(email, "bad_password", user, cancellationToken).ConfigureAwait(false);

            throw new AuthenticationFailedException("bad_password");
        }

        if (!user.CanSignIn(now))
        {
            await RecordFailureAsync(email, "account_disabled", user, cancellationToken).ConfigureAwait(false);
            throw new AuthenticationFailedException("account_disabled");
        }

        // Transparently upgrade a password hashed with older parameters.
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
        }

        user.FailedLoginAttempts = 0;
        user.LockoutEndsAt = null;
        user.LastLoginAt = now;

        var result = await IssueTokensAsync(user, sessionId: Guid.CreateVersion7(), cancellationToken)
            .ConfigureAwait(false);

        await SaveIgnoringTenantAsync(cancellationToken).ConfigureAwait(false);

        await _audit.RecordImmediateAsync(
            AuditAction.Login,
            nameof(User),
            user.Id.ToString(),
            user.DisplayName,
            "Interactive sign-in succeeded.",
            AuditSource.Api,
            AuditOutcome.Success,
            tenantIdOverride: user.TenantId,
            actorUserIdOverride: user.Id,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("User {UserId} in tenant {TenantId} signed in.", user.Id, user.TenantId);

        return result;
    }

    /// <inheritdoc />
    public async Task<AuthenticationResult> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new AuthenticationFailedException("missing_refresh_token");
        }

        var hash = HashToken(refreshToken);
        var now = _clock.UtcNow;

        using var suppression = _context.SuppressTenantFilter();

        {
            var stored = await _context.RefreshTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken)
                .ConfigureAwait(false);

            if (stored?.User is null)
            {
                throw new AuthenticationFailedException("unknown_refresh_token");
            }

            // A token that has already been rotated is either replayed or stolen. Either way the
            // safe response is to kill the whole session chain rather than issue a new pair.
            if (!stored.IsActive(now))
            {
                await RevokeSessionAsync(stored.UserId, stored.SessionId, "token_reuse_detected", cancellationToken)
                    .ConfigureAwait(false);

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await _audit.RecordImmediateAsync(
                    AuditAction.SecurityEvent,
                    nameof(RefreshToken),
                    stored.Id.ToString(),
                    message: "Refresh token reuse detected; session revoked.",
                    source: AuditSource.Api,
                    outcome: AuditOutcome.Denied,
                    tenantIdOverride: stored.TenantId,
                    actorUserIdOverride: stored.UserId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                _logger.LogWarning(
                    "Refresh token reuse detected for user {UserId}; session {SessionId} revoked.",
                    stored.UserId, stored.SessionId);

                throw new AuthenticationFailedException("token_reuse_detected");
            }

            var user = await _context.Users
                .Include(u => u.UserRoles).ThenInclude(r => r.Role)
                .FirstAsync(u => u.Id == stored.UserId, cancellationToken)
                .ConfigureAwait(false);

            if (!user.CanSignIn(now))
            {
                throw new AuthenticationFailedException("account_disabled");
            }

            stored.RevokedAt = now;
            stored.RevokedReason = "rotated";

            var result = await IssueTokensAsync(user, stored.SessionId, cancellationToken).ConfigureAwait(false);
            stored.ReplacedByTokenHash = HashToken(result.RefreshToken);

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
    }

    /// <inheritdoc />
    public async Task SignOutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var hash = HashToken(refreshToken);
        using var suppression = _context.SuppressTenantFilter();

        {
            var stored = await _context.RefreshTokens
                .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken)
                .ConfigureAwait(false);

            if (stored is null)
            {
                return;
            }

            await RevokeSessionAsync(stored.UserId, stored.SessionId, "signed_out", cancellationToken)
                .ConfigureAwait(false);

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await _audit.RecordImmediateAsync(
                AuditAction.Logout,
                nameof(User),
                stored.UserId.ToString(),
                message: "Signed out; session revoked.",
                tenantIdOverride: stored.TenantId,
                actorUserIdOverride: stored.UserId,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<UserProfileDto> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId;

        var user = await _context.Users
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(r => r.Role)
            .Include(u => u.Organization)
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new AuthenticationFailedException("profile_not_found");

        return await BuildProfileAsync(user, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ChangePasswordAsync(
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = _currentUser.UserId;

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new AuthenticationFailedException("profile_not_found");

        if (string.IsNullOrEmpty(user.PasswordHash)
            || _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword)
                == PasswordVerificationResult.Failed)
        {
            await _audit.RecordImmediateAsync(
                AuditAction.SecurityEvent,
                nameof(User),
                user.Id.ToString(),
                user.DisplayName,
                "Password change refused: current password incorrect.",
                outcome: AuditOutcome.Denied,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            throw new AuthenticationFailedException("bad_current_password");
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        user.PasswordChangedAt = _clock.UtcNow;
        user.MustChangePassword = false;

        // Rotating the stamp invalidates every access token already issued for this user, so a
        // token stolen before the password change stops working immediately. The cached copy has
        // to go with it: without the eviction the API keeps comparing against the old value for
        // the length of the cache window, and "immediately" quietly becomes "within a minute".
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await _stampCache.InvalidateAsync(user.Id, cancellationToken).ConfigureAwait(false);

        await RevokeAllSessionsAsync(user.Id, "password_changed", cancellationToken).ConfigureAwait(false);

        _audit.Record(
            AuditAction.SecurityEvent,
            nameof(User),
            user.Id.ToString(),
            user.DisplayName,
            "Password changed; all sessions revoked.");

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Token issuance
    // -----------------------------------------------------------------

    private async Task<AuthenticationResult> IssueTokensAsync(
        User user,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            throw new InvalidOperationException(
                "Auth:SigningKey is not configured. Set it through user secrets, an environment " +
                "variable, or Azure Key Vault before issuing tokens.");
        }

        var profile = await BuildProfileAsync(user, cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.DisplayName),
            new(NexaOpsClaims.UserId, user.Id.ToString()),
            new(NexaOpsClaims.TenantId, user.TenantId.ToString()),
            new(NexaOpsClaims.TenantCode, profile.TenantCode),
            new(NexaOpsClaims.TenantTimeZone, profile.TimeZoneId),
            new(NexaOpsClaims.SecurityStamp, user.SecurityStamp)
        };

        claims.AddRange(profile.Permissions.Select(p => new Claim(NexaOpsClaims.Permission, p)));
        claims.AddRange(profile.Roles.Select(r => new Claim(NexaOpsClaims.RoleCode, r)));
        claims.AddRange(profile.Groups.Select(g => new Claim(NexaOpsClaims.GroupId, g.GroupId.ToString())));

        if (profile.IsPlatformAdministrator)
        {
            claims.Add(new Claim(NexaOpsClaims.PlatformAdministrator, "true"));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        var accessToken = new JsonWebTokenHandler().CreateToken(descriptor);

        // 256 bits of cryptographic randomness; only its SHA-256 hash is stored.
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var refreshExpiry = now.AddDays(_options.RefreshTokenDays);

        _context.RefreshTokens.Add(new RefreshToken
        {
            TenantId = user.TenantId,
            UserId = user.Id,
            SessionId = sessionId,
            TokenHash = HashToken(refreshToken),
            ExpiresAt = refreshExpiry,
            CreatedAt = now,
            CreatedFromIp = _correlation.IpAddress,
            UserAgent = _correlation.UserAgent
        });

        return new AuthenticationResult(accessToken, expiresAt, refreshToken, refreshExpiry, profile);
    }

    /// <summary>
    /// Projects a user into the profile the UI and the token both use, expanding role grants
    /// into effective permissions. Expired role assignments are excluded.
    /// </summary>
    private async Task<UserProfileDto> BuildProfileAsync(User user, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        // Profile assembly can run before the tenant scope exists (during sign-in). The queries
        // below are all constrained by this user's own tenant id, so widening the filter here
        // does not widen what is returned.
        using var suppression = _context.SuppressTenantFilter();

        {
            var roleIds = user.UserRoles
                .Where(ur => ur.IsEffective(now))
                .Select(ur => ur.RoleId)
                .ToList();

            var roles = await _context.Roles
                .AsNoTracking()
                .Where(r => r.TenantId == user.TenantId && roleIds.Contains(r.Id) && !r.IsArchived)
                .Select(r => new { r.Id, r.Code, r.IsPlatformScoped })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var grantedRoleIds = roles.Select(r => r.Id).ToList();

            var permissionCodes = await _context.RolePermissions
                .AsNoTracking()
                .Where(rp => rp.TenantId == user.TenantId && grantedRoleIds.Contains(rp.RoleId))
                .Select(rp => rp.PermissionCode)
                .Distinct()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // A grant for a permission this build no longer enforces is dropped rather than
            // carried in the token, so the claim set always describes reality.
            var permissions = permissionCodes
                .Where(Permissions.IsKnown)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            var groups = await _context.GroupMembers
                .AsNoTracking()
                .Where(m => m.TenantId == user.TenantId && m.UserId == user.Id)
                .Join(
                    _context.Groups.AsNoTracking().Where(g => g.IsActive && !g.IsArchived),
                    m => m.GroupId,
                    g => g.Id,
                    (m, g) => new GroupMembershipDto(g.Id, g.Name, m.IsLead))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var tenant = await _context.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == user.TenantId, cancellationToken)
                .ConfigureAwait(false);

            var organizationName = user.Organization?.Name
                ?? (user.OrganizationId is null
                    ? null
                    : await _context.Organizations.AsNoTracking()
                        .Where(o => o.Id == user.OrganizationId)
                        .Select(o => o.Name)
                        .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false));

            var departmentName = user.Department?.Name
                ?? (user.DepartmentId is null
                    ? null
                    : await _context.Departments.AsNoTracking()
                        .Where(d => d.Id == user.DepartmentId)
                        .Select(d => d.Name)
                        .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false));

            return new UserProfileDto(
                user.Id,
                user.Email,
                user.DisplayName,
                user.FirstName,
                user.LastName,
                user.JobTitle,
                user.AvatarColor,
                user.TenantId,
                tenant?.Code ?? string.Empty,
                tenant?.Name ?? string.Empty,
                user.TimeZoneId ?? tenant?.TimeZoneId ?? "India Standard Time",
                user.Locale ?? tenant?.Locale ?? "en-IN",
                tenant?.CurrencyCode ?? "INR",
                tenant?.DateFormat ?? "dd/MM/yyyy",
                user.OrganizationId,
                organizationName,
                user.DepartmentId,
                departmentName,
                roles.Select(r => r.Code).OrderBy(c => c, StringComparer.Ordinal).ToList(),
                permissions,
                groups,
                user.MustChangePassword,
                roles.Any(r => r.IsPlatformScoped));
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>SHA-256 of the opaque token. Base64 of the raw digest, 44 characters.</summary>
    private static string HashToken(string token)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private async Task RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        string reason,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var tokens = await _context.RefreshTokens
            .Where(t => t.UserId == userId && t.SessionId == sessionId && t.RevokedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var token in tokens)
        {
            token.RevokedAt = now;
            token.RevokedReason = reason;
        }
    }

    private async Task RevokeAllSessionsAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var tokens = await _context.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var token in tokens)
        {
            token.RevokedAt = now;
            token.RevokedReason = reason;
        }
    }

    private async Task<List<Guid>> LoadTenantIdsByCodeAsync(string code, CancellationToken cancellationToken)
        => await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Code == code)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<IReadOnlyList<TenantChoice>> LoadTenantChoicesAsync(
        List<Guid> tenantIds,
        CancellationToken cancellationToken)
        => await _context.Tenants
            .AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id) && t.Status != TenantStatus.Closed)
            .OrderBy(t => t.Name)
            .Select(t => new TenantChoice(t.Code, t.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Saves during sign-in, when no tenant scope has been established yet. The tenant guard in
    /// the interceptor would otherwise refuse writes it cannot attribute.
    /// </summary>
    private async Task SaveIgnoringTenantAsync(CancellationToken cancellationToken)
    {
        using var suppression = _context.SuppressTenantFilter();
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordFailureAsync(
        string email,
        string reason,
        User? user,
        CancellationToken cancellationToken)
        => await _audit.RecordImmediateAsync(
            AuditAction.LoginFailed,
            nameof(User),
            user?.Id.ToString(),
            email,
            $"Sign-in failed: {reason}.",
            AuditSource.Api,
            AuditOutcome.Failure,
            tenantIdOverride: user?.TenantId ?? (_tenant.HasTenant ? _tenant.TenantId : Guid.Empty),
            actorUserIdOverride: user?.Id,
            cancellationToken: cancellationToken).ConfigureAwait(false);
}
