using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Integration;
using NexaOps.Domain.Identity;
using NexaOps.Domain.Integration;
using NexaOps.Infrastructure.Identity;
using NexaOps.Infrastructure.Persistence;

namespace NexaOps.Api.Identity;

/// <summary>
/// Authenticates a machine caller presenting an integration key.
/// <para>
/// Written as an authentication scheme rather than as a bypass, and that is the whole design.
/// A key produces an ordinary <see cref="ClaimsPrincipal"/> carrying the same claims a signed-in
/// person's token carries, so everything downstream — the tenant middleware, the permission
/// attributes, <c>ICurrentUser</c>, the audit interceptor, the global query filters — applies
/// unchanged and without knowing an integration is involved. A separate path for machines is a
/// second set of rules, and the second set is always the one with the hole in it.
/// </para>
/// </summary>
public sealed class IntegrationKeyAuthenticationHandler
    : AuthenticationHandler<IntegrationKeyOptions>
{
    public const string SchemeName = "IntegrationKey";


    private readonly IIntegrationKeyRepository _keys;
    private readonly IDbContextFactory<NexaOpsDbContext> _contextFactory;
    private readonly IDateTimeProvider _clock;

    public IntegrationKeyAuthenticationHandler(
        IOptionsMonitor<IntegrationKeyOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IIntegrationKeyRepository keys,
        IDbContextFactory<NexaOpsDbContext> contextFactory,
        IDateTimeProvider clock)
        : base(options, logger, encoder)
    {
        _keys = keys;
        _contextFactory = contextFactory;
        _clock = clock;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(NexaOpsAuthentication.KeyHeader, out var header))
        {
            // No key presented is not a failure, it is simply not this scheme's request.
            return AuthenticateResult.NoResult();
        }

        var presented = header.ToString().Trim();

        if (string.IsNullOrEmpty(presented))
        {
            return AuthenticateResult.Fail("No key presented.");
        }

        var key = await _keys
            .FindByHashAsync(IntegrationKeyService.Hash(presented), Context.RequestAborted)
            .ConfigureAwait(false);

        if (key is null)
        {
            // Deliberately the same outcome and the same message as a revoked or expired key.
            // Distinguishing "no such key" from "that key is disabled" tells whoever is probing
            // which of their guesses was once real.
            Logger.LogWarning("An integration key was presented that does not match any issued key.");
            return AuthenticateResult.Fail("The key is not valid.");
        }

        var now = _clock.UtcNow;

        if (!key.IsUsableAt(now))
        {
            Logger.LogWarning(
                "Integration key {Prefix} was presented but is {Reason}.",
                key.Prefix,
                key.UnusableReason(now));

            return AuthenticateResult.Fail("The key is not valid.");
        }

        if (key.ServiceAccount is null || key.ServiceAccount.Status != UserStatus.Active)
        {
            // Disabling the service account disables every key that acts as it. That is the
            // intended way to cut off an integration in a hurry without hunting for its keys.
            Logger.LogWarning(
                "Integration key {Prefix} was presented but its service account is not active.", key.Prefix);

            return AuthenticateResult.Fail("The key is not valid.");
        }

        var principal = await BuildPrincipalAsync(key).ConfigureAwait(false);

        // Recorded after the decision, and never allowed to fail it: knowing when a key was last
        // used is useful, and losing that is not a reason to refuse a valid caller.
        try
        {
            await _keys.TouchAsync(key.Id, now, Context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not stamp last-used on integration key {Prefix}.", key.Prefix);
        }

        // Carried on the request so the endpoint can check the key's scope, and so a log line
        // can say which key did something.
        Context.Items["IntegrationKeyId"] = key.Id;
        Context.Items["IntegrationKeyScope"] = key.Scope;
        Context.Items["IntegrationKeyPrefix"] = key.Prefix;

        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    /// <summary>
    /// Builds the principal from the service account, with exactly the account's permissions.
    /// <para>
    /// Read fresh rather than cached: a key is long-lived, so a role removed from its service
    /// account has to take effect on the next call. The equivalent for a person is the security
    /// stamp; for a key it is simply that nothing is remembered between calls.
    /// </para>
    /// </summary>
    private async Task<ClaimsPrincipal> BuildPrincipalAsync(IntegrationKey key)
    {
        await using var context = await _contextFactory
            .CreateDbContextAsync(Context.RequestAborted)
            .ConfigureAwait(false);

        using var _ = context.SuppressTenantFilter();

        var now = _clock.UtcNow;

        var permissions = await context.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == key.ServiceAccountUserId
                         && (ur.ExpiresAt == null || ur.ExpiresAt > now))
            .SelectMany(ur => ur.Role!.RolePermissions.Select(rp => rp.PermissionCode))
            .Distinct()
            .ToListAsync(Context.RequestAborted)
            .ConfigureAwait(false);

        var tenant = await context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == key.TenantId)
            .Select(t => new { t.Code, t.TimeZoneId })
            .FirstOrDefaultAsync(Context.RequestAborted)
            .ConfigureAwait(false);

        var claims = new List<Claim>
        {
            new(NexaOpsClaims.UserId, key.ServiceAccountUserId.ToString()),
            new(NexaOpsClaims.TenantId, key.TenantId.ToString()),
            new(NexaOpsClaims.TenantCode, tenant?.Code ?? string.Empty),
            new(NexaOpsClaims.TenantTimeZone, tenant?.TimeZoneId ?? "India Standard Time"),
            new(ClaimTypes.Name, key.ServiceAccount?.DisplayName ?? key.Name),
            new(ClaimTypes.Email, key.ServiceAccount?.Email ?? string.Empty)
        };

        claims.AddRange(permissions.Select(p => new Claim(NexaOpsClaims.Permission, p)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
    }
}

/// <summary>No options today. The type exists because the scheme needs one.</summary>
public sealed class IntegrationKeyOptions : AuthenticationSchemeOptions;
