using NexaOps.Application.Abstractions;

namespace NexaOps.Infrastructure.Identity;

/// <summary>
/// A tenant context that reports no tenant.
/// <para>
/// Used by design-time tooling and by tests that build a context directly. It fails closed:
/// <see cref="HasTenant"/> is false, so every tenant query filter matches nothing and the tenant
/// guard refuses every write. That is the correct behaviour for a context with no tenant - the
/// alternative, an empty <see cref="Guid"/> treated as a real tenant, would look like an empty
/// result set rather than the misconfiguration it is.
/// </para>
/// </summary>
public sealed class NoTenantContext : ITenantContext
{
    public static readonly NoTenantContext Instance = new();

    private NoTenantContext()
    {
    }

    /// <inheritdoc />
    public bool HasTenant => false;

    /// <inheritdoc />
    public Guid TenantId => throw new InvalidOperationException(
        "No tenant scope is established. This context was created outside a request, so it " +
        "cannot be used for tenant-scoped work.");

    /// <inheritdoc />
    public string TenantCode => string.Empty;

    /// <inheritdoc />
    public string TimeZoneId => "India Standard Time";

    /// <inheritdoc />
    public string Locale => "en-IN";

    /// <inheritdoc />
    public string CurrencyCode => "INR";

    /// <inheritdoc />
    public bool IsPlatformImpersonation => false;
}
