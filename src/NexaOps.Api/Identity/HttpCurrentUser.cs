using System.Security.Claims;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Infrastructure.Identity;

namespace NexaOps.Api.Identity;

/// <summary>
/// The caller, projected from the validated security token.
/// <para>
/// Every value here comes from a signed claim. Nothing is read from a header, a query string or
/// a body, so changing identity or tenant would require forging a signature rather than editing
/// a request.
/// </para>
/// <para>
/// The principal is read on every access rather than captured in the constructor. That matters:
/// this service is scoped, and middleware that runs before authentication - the exception
/// handler, which needs the audit service - can cause it to be constructed while the request is
/// still anonymous. Capturing the principal at construction froze that anonymous identity for
/// the rest of the request and made every permission check fail.
/// </para>
/// </summary>
public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    // Derived sets are memoised against the principal they came from, so the common case costs
    // one reference comparison rather than re-walking the claim collection on every check.
    private ClaimsPrincipal? _cachedFor;
    private IReadOnlySet<string>? _permissions;
    private IReadOnlySet<Guid>? _groupIds;

    public HttpCurrentUser(IHttpContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    /// <inheritdoc />
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    /// <inheritdoc />
    public Guid UserId => UserIdOrNull
        ?? throw new InvalidOperationException(
            "No authenticated user. An anonymous request reached a service that requires identity.");

    /// <inheritdoc />
    public Guid? UserIdOrNull
        => Guid.TryParse(Principal?.FindFirstValue(NexaOpsClaims.UserId), out var id) ? id : null;

    /// <inheritdoc />
    public string Email => Principal?.FindFirstValue(ClaimTypes.Email)
                           ?? Principal?.FindFirstValue("email")
                           ?? string.Empty;

    /// <inheritdoc />
    public string DisplayName => Principal?.FindFirstValue("name")
                                 ?? Principal?.FindFirstValue(ClaimTypes.Name)
                                 ?? Email;

    /// <inheritdoc />
    public IReadOnlySet<string> Permissions
    {
        get
        {
            Refresh();
            return _permissions ?? new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <inheritdoc />
    public IReadOnlySet<Guid> GroupIds
    {
        get
        {
            Refresh();
            return _groupIds ?? new HashSet<Guid>();
        }
    }

    /// <inheritdoc />
    public bool IsPlatformAdministrator
        => Principal?.HasClaim(NexaOpsClaims.PlatformAdministrator, "true") == true;

    /// <inheritdoc />
    public bool HasPermission(string permission) => Permissions.Contains(permission);

    /// <inheritdoc />
    public void DemandPermission(string permission)
    {
        if (!HasPermission(permission))
        {
            throw new ForbiddenException(permission);
        }
    }

    /// <summary>Rebuilds the derived claim sets whenever the underlying principal changes.</summary>
    private void Refresh()
    {
        var principal = Principal;

        if (ReferenceEquals(principal, _cachedFor) && _permissions is not null)
        {
            return;
        }

        _cachedFor = principal;

        _permissions = principal?
            .FindAll(NexaOpsClaims.Permission)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);

        _groupIds = principal?
            .FindAll(NexaOpsClaims.GroupId)
            .Select(c => Guid.TryParse(c.Value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet()
            ?? [];
    }
}

/// <summary>Correlation identity for the current HTTP request.</summary>
public sealed class HttpCorrelationContext : ICorrelationContext
{
    /// <summary>Request header clients may send to join their own trace to ours.</summary>
    public const string HeaderName = "X-Correlation-Id";

    private readonly IHttpContextAccessor _accessor;

    public HttpCorrelationContext(IHttpContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    /// <inheritdoc />
    public string CorrelationId
        => _accessor.HttpContext?.Items[HeaderName] as string
           ?? _accessor.HttpContext?.TraceIdentifier
           ?? "unknown";

    /// <inheritdoc />
    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    /// <inheritdoc />
    public string? UserAgent
    {
        get
        {
            var raw = _accessor.HttpContext?.Request.Headers.UserAgent.ToString();
            return string.IsNullOrWhiteSpace(raw) ? null : raw.Length > 512 ? raw[..512] : raw;
        }
    }
}
