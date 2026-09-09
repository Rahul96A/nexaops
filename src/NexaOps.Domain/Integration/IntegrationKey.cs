using NexaOps.Domain.Common;
using NexaOps.Domain.Identity;

namespace NexaOps.Domain.Integration;

/// <summary>
/// A machine credential: how a system that is not a person authenticates to NexaOps.
/// <para>
/// A key does not carry permissions of its own. It names a service account, and the caller
/// authenticated by the key holds exactly that account's roles — no more, and nothing separate
/// to keep in step. A second permission model attached to keys would drift from the first, and
/// the drift would only be discovered when something was allowed that should not have been.
/// </para>
/// </summary>
public sealed class IntegrationKey : TenantEntity
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// The first few characters of the key, stored in the clear so a person can tell two keys
    /// apart in a list and match one against a log line. Not enough of the key to be useful to
    /// anybody who reads it.
    /// </summary>
    public required string Prefix { get; set; }

    /// <summary>
    /// SHA-256 of the full key.
    /// <para>
    /// Hashed, not encrypted, and never recoverable: a key an administrator can read back is a
    /// credential with two owners. Losing it means issuing a new one, which is the correct cost.
    /// </para>
    /// <para>
    /// A plain hash rather than a slow KDF, deliberately. This is a 256-bit random value, not a
    /// human-chosen password: there is no dictionary to run and no meaningful gain from making
    /// each verification expensive — while there is a real cost, since this runs on every call
    /// from an integration that may be making thousands.
    /// </para>
    /// </summary>
    public required string KeyHash { get; set; }

    /// <summary>
    /// The service account this key acts as. Its roles decide what the key can do.
    /// </summary>
    public Guid ServiceAccountUserId { get; set; }

    /// <summary>
    /// What the key may be used for, beyond its service account's permissions.
    /// <para>
    /// A second, narrower gate on top of the account's roles: a key issued for inbound email
    /// cannot be pointed at the incident API even if its service account could. Keys leak into
    /// scripts and CI logs, and a leaked key that can do one thing is a smaller problem than one
    /// that can do everything its account can.
    /// </para>
    /// </summary>
    public IntegrationScope Scope { get; set; } = IntegrationScope.InboundEmail;

    /// <summary>Null for a key that does not expire. Set is better; not everybody will.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// Updated when the key is used, so an administrator can see which keys are dead.
    /// <para>
    /// Written at most once a minute rather than on every call: a busy integration would
    /// otherwise turn every request into a write, and knowing a key was used "within the last
    /// minute" is as useful as knowing the second.
    /// </para>
    /// </summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid? RevokedByUserId { get; set; }

    public User? ServiceAccount { get; set; }

    /// <summary>Whether this key may authenticate right now.</summary>
    public bool IsUsableAt(DateTimeOffset now)
        => RevokedAt is null && !IsArchived && (ExpiresAt is null || ExpiresAt > now);

    /// <summary>Why it cannot, for the log. Never returned to the caller.</summary>
    public string? UnusableReason(DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return "revoked";
        }

        if (IsArchived)
        {
            return "archived";
        }

        return ExpiresAt is not null && ExpiresAt <= now ? "expired" : null;
    }
}

/// <summary>What an integration key is allowed to reach.</summary>
public enum IntegrationScope
{
    /// <summary>Deliver inbound email into the service desk. Nothing else.</summary>
    InboundEmail = 1
}
