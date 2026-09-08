using NexaOps.Domain.Common;

namespace NexaOps.Domain.Identity;

/// <summary>A department within an organization. Departments may nest arbitrarily deep.</summary>
public class Department : TenantEntity
{
    public Guid OrganizationId { get; set; }
    public Guid? ParentDepartmentId { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Head of department. Used as the default approver for departmental requests.</summary>
    public Guid? ManagerUserId { get; set; }

    /// <summary>Cost centre reference used when charging fulfilment back to the department.</summary>
    public string? CostCentre { get; set; }

    public bool IsActive { get; set; } = true;

    public Organization? Organization { get; set; }
    public Department? ParentDepartment { get; set; }
    public ICollection<Department> ChildDepartments { get; set; } = new List<Department>();
}
