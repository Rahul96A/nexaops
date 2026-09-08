using NexaOps.Domain.Common;

namespace NexaOps.Domain.Identity;

/// <summary>
/// The root of the isolation hierarchy: Platform -> Tenant -> Organization -> Department -> User.
/// A tenant is one paying customer. It is deliberately NOT <see cref="ITenantOwned"/>; access to
/// tenant rows themselves is gated by the platform administration permission set.
/// </summary>
public class Tenant : Entity, IAuditable, IConcurrencyAware
{
    /// <summary>Short immutable identifier used in URLs and support conversations, e.g. acme-in.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Active;

    /// <summary>Primary email domain used to route SSO sign-ins, e.g. acmetech.in.</summary>
    public string? PrimaryDomain { get; set; }

    /// <summary>Entra ID directory (tenant) GUID, when the customer uses enterprise SSO.</summary>
    public string? EntraTenantId { get; set; }

    // --- Localisation. Defaults target the Indian enterprise market. ---
    public string TimeZoneId { get; set; } = "India Standard Time";
    public string Locale { get; set; } = "en-IN";
    public string CurrencyCode { get; set; } = "INR";
    public string DateFormat { get; set; } = "dd/MM/yyyy";

    /// <summary>Where customer data is stored. Surfaced for data-residency conversations.</summary>
    public string DataRegion { get; set; } = "centralindia";

    /// <summary>
    /// Retention window for closed transactional records, in days. Exposed as a configurable
    /// control so customers can align it with their own DPDP retention policy.
    /// </summary>
    public int RecordRetentionDays { get; set; } = 2555;

    /// <summary>Retention window for audit events, in days.</summary>
    public int AuditRetentionDays { get; set; } = 2555;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public byte[]? RowVersion { get; set; }

    public ICollection<Organization> Organizations { get; set; } = new List<Organization>();
}

public enum TenantStatus
{
    Active = 1,
    Trial = 2,
    Suspended = 3,
    Closed = 4
}
