using NexaOps.Domain.Common;

namespace NexaOps.Domain.ServiceDesk;

/// <summary>
/// A classification bucket, scoped to a module so that incident and change taxonomies can
/// diverge without one polluting the other.
/// </summary>
public class Category : TenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Which record type this category applies to.</summary>
    public ServiceModule Module { get; set; } = ServiceModule.Incident;

    /// <summary>Default routing for records classified here. Applied when nothing more specific matches.</summary>
    public Guid? DefaultAssignmentGroupId { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Subcategory> Subcategories { get; set; } = new List<Subcategory>();
}

/// <summary>A second-level classification beneath a <see cref="Category"/>.</summary>
public class Subcategory : TenantEntity
{
    public Guid CategoryId { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Overrides the parent category routing when set.</summary>
    public Guid? DefaultAssignmentGroupId { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public Category? Category { get; set; }
}

/// <summary>The record types that share the platform taxonomy, SLA engine and audit trail.</summary>
public enum ServiceModule
{
    Incident = 1,
    Request = 2,
    Problem = 3,
    Change = 4,
    Knowledge = 5,
    Asset = 6,
    ConfigurationItem = 7
}
