using NexaOps.Domain.Common;

namespace NexaOps.Domain.Identity;

/// <summary>
/// A rotating, single-use refresh token for the local authentication mode.
/// <para>
/// Only the SHA-256 hash of the token is stored, so a database disclosure does not yield
/// usable credentials. Presenting an already-rotated token is treated as theft and revokes
/// the entire session chain.
/// </para>
/// </summary>
public class RefreshToken : TenantEntity
{
    public Guid UserId { get; set; }

    /// <summary>Base64 SHA-256 of the opaque token value. The plaintext is never persisted.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Groups the rotation chain so one stolen token revokes the whole session.</summary>
    public Guid SessionId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public string? CreatedFromIp { get; set; }
    public string? UserAgent { get; set; }

    public User? User { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
