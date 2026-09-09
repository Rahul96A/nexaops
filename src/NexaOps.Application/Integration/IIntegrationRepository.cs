using NexaOps.Application.Common;
using NexaOps.Domain.Integration;

namespace NexaOps.Application.Integration;

/// <summary>Persistence for what arrives from outside.</summary>
public interface IInboundEmailRepository
{
    /// <summary>
    /// The message with this provider-assigned identifier, if it has already been handled.
    /// <para>
    /// The idempotency lookup, and the reply-threading lookup: the same identifier that stops a
    /// retry creating a second ticket is the one a reply's <c>In-Reply-To</c> points at.
    /// </para>
    /// </summary>
    Task<InboundMessage?> FindByExternalIdAsync(string externalMessageId, CancellationToken ct = default);

    void Add(InboundMessage message);

    Task<PagedResult<InboundMessageDto>> SearchAsync(InboundMessageQuery query, CancellationToken ct = default);
}

/// <param name="Id">User identifier.</param>
/// <param name="Email">Sign-in address.</param>
/// <param name="DisplayName">Display name.</param>
public sealed record IntegrationUserDto(Guid Id, string Email, string DisplayName);

/// <param name="Id">Record identifier.</param>
/// <param name="Number">Human reference.</param>
/// <param name="IsOpen">False once resolved, closed or cancelled.</param>
public sealed record IntegrationRecordDto(Guid Id, string Number, bool IsOpen);

/// <summary>
/// The look-ups an integration needs before it can act on somebody's behalf.
/// <para>
/// Separate from the service desk reference repository because the questions are different: an
/// integration starts from an email address and a record number written by a stranger, not from
/// an identifier the UI already had.
/// </para>
/// </summary>
public interface IIntegrationDirectory
{
    /// <summary>
    /// The active account using this address, or nothing.
    /// <para>
    /// Tenant-filtered. An address belonging to a neighbouring tenant reads as unknown, so a
    /// message cannot be attributed across a boundary.
    /// </para>
    /// </summary>
    Task<IntegrationUserDto?> FindActiveUserByEmailAsync(string email, CancellationToken ct = default);

    Task<IntegrationRecordDto?> FindIncidentByNumberAsync(string number, CancellationToken ct = default);

    /// <summary>The active account with this identifier, for validating a key's service account.</summary>
    Task<IntegrationUserDto?> FindActiveUserByIdAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>Persistence for machine credentials.</summary>
public interface IIntegrationKeyRepository
{
    /// <summary>
    /// Finds a key by its hash, across every tenant.
    /// <para>
    /// Deliberately unfiltered: authentication happens before a tenant scope exists, and the key
    /// is what establishes one. The lookup is on a hash of a 256-bit random value, so it reveals
    /// nothing about which tenants exist — and the tenant it returns is then used to scope
    /// everything that follows.
    /// </para>
    /// </summary>
    Task<IntegrationKey?> FindByHashAsync(string keyHash, CancellationToken ct = default);

    Task<IReadOnlyList<IntegrationKey>> GetForTenantAsync(CancellationToken ct = default);

    Task<IntegrationKey?> GetAsync(Guid id, CancellationToken ct = default);

    void Add(IntegrationKey key);

    /// <summary>
    /// Stamps the last-used time without going through the ordinary unit of work.
    /// <para>
    /// Authentication runs outside a tenant scope and outside the request's transaction, and it
    /// must not be able to fail the request it is authenticating. Throttled to at most one write
    /// per key per minute — a busy integration would otherwise turn every call into a write, and
    /// "used within the last minute" is as useful as "used at 14:03:07".
    /// </para>
    /// </summary>
    Task TouchAsync(Guid keyId, DateTimeOffset now, CancellationToken ct = default);
}
