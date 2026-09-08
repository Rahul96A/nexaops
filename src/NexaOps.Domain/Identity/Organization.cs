using NexaOps.Domain.Common;

namespace NexaOps.Domain.Identity;

/// <summary>
/// A legal entity or business unit within a tenant. A tenant with several registered
/// companies (common in Indian groups) models each as an organization.
/// </summary>
public class Organization : TenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }

    /// <summary>GSTIN, 15 characters. Configuration foundation for billing and invoicing.</summary>
    public string? GstIdentificationNumber { get; set; }

    /// <summary>Permanent Account Number, 10 characters.</summary>
    public string? PermanentAccountNumber { get; set; }
    public string? CorporateIdentityNumber { get; set; }

    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }

    /// <summary>Indian state or union territory code, e.g. MH. See IndianState.</summary>
    public string? StateCode { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "IN";

    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }

    /// <summary>Business calendar used for SLA clocks when a policy does not name one explicitly.</summary>
    public Guid? DefaultBusinessCalendarId { get; set; }

    public bool IsActive { get; set; } = true;

    public Tenant? Tenant { get; set; }
    public ICollection<Department> Departments { get; set; } = new List<Department>();
}
