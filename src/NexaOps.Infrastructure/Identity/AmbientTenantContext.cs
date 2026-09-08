using NexaOps.Application.Abstractions;

namespace NexaOps.Infrastructure.Identity;

/// <summary>
/// The scoped holder for the ambient tenant.
/// <para>
/// In a web request the authentication middleware establishes this from the validated token.
/// In a background job the worker establishes it explicitly per tenant. Nothing else can change
/// it, because <see cref="ITenantContext"/> - the interface application services receive - has
/// no setter at all.
/// </para>
/// </summary>
public sealed class AmbientTenantContext : ITenantContext, ITenantContextSetter
{
    private State? _state;

    /// <inheritdoc />
    public bool HasTenant => _state is not null;

    /// <inheritdoc />
    public Guid TenantId => _state?.TenantId
        ?? throw new InvalidOperationException(
            "No tenant scope has been established. This indicates a request that reached a " +
            "tenant-scoped service without authentication, or a background job that failed to " +
            "open a tenant scope.");

    /// <inheritdoc />
    public string TenantCode => _state?.TenantCode ?? string.Empty;

    /// <inheritdoc />
    public string TimeZoneId => _state?.TimeZoneId ?? "India Standard Time";

    /// <inheritdoc />
    public string Locale => _state?.Locale ?? "en-IN";

    /// <inheritdoc />
    public string CurrencyCode => _state?.CurrencyCode ?? "INR";

    /// <inheritdoc />
    public bool IsPlatformImpersonation => _state?.IsPlatformImpersonation ?? false;

    /// <inheritdoc />
    public IDisposable BeginScope(
        Guid tenantId,
        string tenantCode,
        string timeZoneId,
        bool isPlatformImpersonation = false)
    {
        var previous = _state;

        _state = new State(
            tenantId,
            tenantCode,
            string.IsNullOrWhiteSpace(timeZoneId) ? "India Standard Time" : timeZoneId,
            "en-IN",
            "INR",
            isPlatformImpersonation);

        return new Scope(this, previous);
    }

    private sealed record State(
        Guid TenantId,
        string TenantCode,
        string TimeZoneId,
        string Locale,
        string CurrencyCode,
        bool IsPlatformImpersonation);

    private sealed class Scope : IDisposable
    {
        private readonly AmbientTenantContext _owner;
        private readonly State? _previous;
        private bool _disposed;

        public Scope(AmbientTenantContext owner, State? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _owner._state = _previous;
            _disposed = true;
        }
    }
}
