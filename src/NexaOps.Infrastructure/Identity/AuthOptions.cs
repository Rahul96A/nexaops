using System.ComponentModel.DataAnnotations;

namespace NexaOps.Infrastructure.Identity;

/// <summary>
/// Authentication configuration.
/// <para>
/// The signing key is a secret. It must come from Azure Key Vault, an environment variable, or
/// user secrets - never from a checked-in appsettings file. Startup validation refuses to run
/// outside Development without one, so a deployment cannot accidentally use a weak default.
/// </para>
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary><c>Local</c> for first-party JWT, <c>EntraId</c> for Microsoft Entra ID.</summary>
    public AuthMode Mode { get; set; } = AuthMode.Local;

    [Required]
    public string Issuer { get; set; } = "https://nexaops.local";

    [Required]
    public string Audience { get; set; } = "nexaops-api";

    /// <summary>
    /// HMAC-SHA256 signing key for locally issued tokens. At least 32 bytes of entropy.
    /// Supplied through configuration, never committed.
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>
    /// Access token lifetime. Kept short because permissions are embedded in the token; a
    /// revoked role takes at most this long to stop working, and the security stamp check
    /// closes the window sooner for credential changes.
    /// </summary>
    [Range(1, 240)]
    public int AccessTokenMinutes { get; set; } = 30;

    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>Failed attempts before the account is locked.</summary>
    [Range(3, 20)]
    public int MaxFailedLoginAttempts { get; set; } = 5;

    [Range(1, 1440)]
    public int LockoutMinutes { get; set; } = 15;

    [Range(8, 128)]
    public int MinimumPasswordLength { get; set; } = 12;

    /// <summary>Entra ID settings. Only read when <see cref="Mode"/> is <see cref="AuthMode.EntraId"/>.</summary>
    public EntraIdOptions EntraId { get; set; } = new();
}

public enum AuthMode
{
    /// <summary>First-party JWT issued by NexaOps. Used for demos, pilots, and customers without Entra ID.</summary>
    Local = 0,

    /// <summary>Microsoft Entra ID. MFA and Conditional Access are enforced by Entra.</summary>
    EntraId = 1
}

/// <summary>Microsoft Entra ID configuration. No secrets: the API validates tokens, it does not mint them.</summary>
public sealed class EntraIdOptions
{
    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    /// <summary>Directory (tenant) id, or <c>organizations</c> for multi-tenant.</summary>
    public string? TenantId { get; set; }

    /// <summary>Application (client) id of the API registration.</summary>
    public string? ClientId { get; set; }

    /// <summary>Expected audience, usually <c>api://{ClientId}</c>.</summary>
    public string? Audience { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId);
}
