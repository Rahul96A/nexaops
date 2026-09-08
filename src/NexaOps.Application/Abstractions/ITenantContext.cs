namespace NexaOps.Application.Abstractions;

/// <summary>
/// The ambient tenant for the current unit of work.
/// <para>
/// The implementation resolves this from the authenticated server-side principal only. It is
/// never populated from a request body, query string, route value or header, because a value
/// the client controls is a value an attacker controls.
/// </para>
/// </summary>
public interface ITenantContext
{
    /// <summary>True once a tenant has been established for this scope.</summary>
    bool HasTenant { get; }

    /// <summary>
    /// The current tenant. Throws when no tenant is established, rather than returning
    /// <see cref="Guid.Empty"/> - a silent empty tenant would match nothing and look like an
    /// empty result set instead of the bug it is.
    /// </summary>
    Guid TenantId { get; }

    string TenantCode { get; }

    /// <summary>IANA or Windows timezone id used to render timestamps for this tenant.</summary>
    string TimeZoneId { get; }

    string Locale { get; }
    string CurrencyCode { get; }

    /// <summary>
    /// True while a platform operator is deliberately acting inside a customer tenant. Every
    /// write performed in this state is audited with <c>AuditAction.Impersonation</c>.
    /// </summary>
    bool IsPlatformImpersonation { get; }
}

/// <summary>
/// Lets trusted infrastructure code establish a tenant scope - background jobs, the demo data
/// seeder, and platform administration. Deliberately separate from <see cref="ITenantContext"/>
/// so that ordinary application services cannot change tenant simply by holding the context.
/// </summary>
public interface ITenantContextSetter
{
    /// <summary>
    /// Establishes the ambient tenant for the current scope, returning a token that restores
    /// the previous value when disposed.
    /// </summary>
    IDisposable BeginScope(Guid tenantId, string tenantCode, string timeZoneId, bool isPlatformImpersonation = false);
}
