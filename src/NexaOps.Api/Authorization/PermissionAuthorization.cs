using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using NexaOps.Application.Security;
using NexaOps.Infrastructure.Identity;

namespace NexaOps.Api.Authorization;

/// <summary>
/// Requires the caller to hold a specific permission.
/// <para>
/// Applied as <c>[RequiresPermission(Permissions.IncidentAssign)]</c>. The attribute is the
/// coarse gate at the HTTP boundary; the application service performs the same check again,
/// because an endpoint is not the only way into a use case.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresPermissionAttribute : AuthorizeAttribute
{
    /// <summary>Prefix that <see cref="PermissionPolicyProvider"/> recognises as a permission policy.</summary>
    public const string PolicyPrefix = "nexaops.permission:";

    public RequiresPermissionAttribute(string permission)
    {
        Permission = permission;
        Policy = PolicyPrefix + permission;
    }

    public string Permission { get; }
}

/// <summary>The authorization requirement carrying the permission code.</summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission) => Permission = permission;

    public string Permission { get; }
}

/// <summary>
/// Builds an authorization policy on demand for any permission code.
/// <para>
/// Without this, every one of the fifty-odd permissions would need a hand-written
/// <c>AddPolicy</c> call at startup, and a new permission would silently fail open until
/// someone remembered to add one. Generating policies from the attribute removes that failure
/// mode entirely.
/// </para>
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
        => _fallback = new DefaultAuthorizationPolicyProvider(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(RequiresPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            return _fallback.GetPolicyAsync(policyName);
        }

        var permission = policyName[RequiresPermissionAttribute.PolicyPrefix.Length..];

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}

/// <summary>
/// Decides permission requirements by looking for the permission claim the token carries.
/// <para>
/// Because permissions travel as signed claims, this check needs no database round trip. The
/// token is short-lived and the security stamp is validated on each request, so a revoked
/// permission stops working promptly rather than at token expiry.
/// </para>
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly ILogger<PermissionAuthorizationHandler> _logger;

    public PermissionAuthorizationHandler(ILogger<PermissionAuthorizationHandler> logger)
        => _logger = logger;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        var holdsPermission = context.User.Claims.Any(c =>
            c.Type == NexaOpsClaims.Permission
            && string.Equals(c.Value, requirement.Permission, StringComparison.Ordinal));

        if (holdsPermission)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // A denial is a security-relevant event. It is logged here and audited by the
        // exception handler when the resulting 403 is produced.
        _logger.LogInformation(
            "Authorization denied: caller lacks {Permission}.",
            requirement.Permission);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Guards platform-scoped endpoints, which act across tenants.
/// Separate from ordinary permissions because holding it changes the isolation model itself.
/// </summary>
public sealed class PlatformAdministratorRequirement : IAuthorizationRequirement;

/// <inheritdoc cref="PlatformAdministratorRequirement" />
public sealed class PlatformAdministratorHandler : AuthorizationHandler<PlatformAdministratorRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PlatformAdministratorRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.User.HasClaim(NexaOpsClaims.PlatformAdministrator, "true")
            && context.User.HasClaim(NexaOpsClaims.Permission, Permissions.PlatformTenantManage))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
