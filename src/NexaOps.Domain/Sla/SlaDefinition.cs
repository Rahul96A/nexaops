using NexaOps.Domain.Common;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Domain.Sla;

/// <summary>
/// A commitment: "resolve a P1 within four working hours". The definition holds the duration
/// and the calendar; <see cref="SlaPolicy"/> decides which records it applies to.
/// </summary>
public class SlaDefinition : TenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ServiceModule Module { get; set; } = ServiceModule.Incident;
    public SlaTargetType TargetType { get; set; } = SlaTargetType.Resolution;

    /// <summary>The commitment, in minutes. Counted against the business calendar.</summary>
    public int DurationMinutes { get; set; }

    /// <summary>Calendar used to count the duration. Null means the tenant default calendar.</summary>
    public Guid? BusinessCalendarId { get; set; }

    /// <summary>
    /// Percentage of the duration at which a warning fires, e.g. 80 means notify once
    /// four fifths of the allowance is consumed. Clamped to 1-99 by validation.
    /// </summary>
    public int WarningThresholdPercent { get; set; } = 80;

    /// <summary>When true, the clock stops while the record is in a pending state.</summary>
    public bool PauseWhenPending { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public BusinessCalendar? BusinessCalendar { get; set; }
}

/// <summary>
/// Binds an <see cref="SlaDefinition"/> to the records it governs. Policies are evaluated in
/// <see cref="Order"/> and the first match wins, so a specific policy can be placed above a
/// general one without deleting the general one.
/// </summary>
public class SlaPolicy : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid SlaDefinitionId { get; set; }

    public ServiceModule Module { get; set; } = ServiceModule.Incident;

    // --- Match conditions. A null condition means "any". ---
    public Priority? Priority { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? SubcategoryId { get; set; }
    public Guid? AssignmentGroupId { get; set; }
    public Guid? OrganizationId { get; set; }

    /// <summary>Lower numbers are evaluated first.</summary>
    public int Order { get; set; }

    public bool IsActive { get; set; } = true;

    public SlaDefinition? SlaDefinition { get; set; }

    /// <summary>
    /// True when every populated condition on this policy matches the supplied record facts.
    /// Conditions left null are wildcards.
    /// </summary>
    public bool Matches(
        ServiceModule module,
        Priority priority,
        Guid? categoryId,
        Guid? subcategoryId,
        Guid? assignmentGroupId,
        Guid? organizationId)
        => IsActive
           && !IsArchived
           && Module == module
           && (Priority is null || Priority == priority)
           && (CategoryId is null || CategoryId == categoryId)
           && (SubcategoryId is null || SubcategoryId == subcategoryId)
           && (AssignmentGroupId is null || AssignmentGroupId == assignmentGroupId)
           && (OrganizationId is null || OrganizationId == organizationId);
}
