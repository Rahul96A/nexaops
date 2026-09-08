namespace NexaOps.Infrastructure.Identity;

/// <summary>
/// Claim types NexaOps issues and reads.
/// <para>
/// Tenant and permissions are carried as claims on a signed token rather than being read from
/// the request, which is what makes "tenant context comes from server-side identity" true in
/// practice: to change tenant an attacker would have to forge a signature.
/// </para>
/// </summary>
public static class NexaOpsClaims
{
    /// <summary>NexaOps user id.</summary>
    public const string UserId = "nexaops:uid";

    /// <summary>NexaOps tenant id. The only source the tenant context ever reads.</summary>
    public const string TenantId = "nexaops:tid";

    /// <summary>Tenant code, for display and support.</summary>
    public const string TenantCode = "nexaops:tcode";

    /// <summary>Tenant timezone, so the UI can render without an extra call.</summary>
    public const string TenantTimeZone = "nexaops:ttz";

    /// <summary>One claim per effective permission code.</summary>
    public const string Permission = "nexaops:perm";

    /// <summary>One claim per assignment group the user belongs to.</summary>
    public const string GroupId = "nexaops:grp";

    /// <summary>Role code, for display only. No authorization decision reads this.</summary>
    public const string RoleCode = "nexaops:role";

    /// <summary>
    /// The user's security stamp at issue time. A mismatch against the stored stamp means
    /// credentials or role assignments changed, and the token is rejected immediately.
    /// </summary>
    public const string SecurityStamp = "nexaops:stamp";

    /// <summary>Present when the user holds a platform-scoped role.</summary>
    public const string PlatformAdministrator = "nexaops:platform";
}
