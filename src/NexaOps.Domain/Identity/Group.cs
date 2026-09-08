using NexaOps.Domain.Common;

namespace NexaOps.Domain.Identity;

/// <summary>
/// A team. Assignment groups own queues of work; approval groups gate change and request
/// approvals; notification groups are broadcast targets.
/// </summary>
public class Group : TenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public GroupType Type { get; set; } = GroupType.Assignment;

    /// <summary>Shared mailbox for the team, used by email notification rules.</summary>
    public string? Email { get; set; }

    public Guid? ManagerUserId { get; set; }

    /// <summary>Agent who receives work routed to the group but not to a named person.</summary>
    public Guid? DefaultAssigneeUserId { get; set; }

    /// <summary>Business calendar governing SLA clocks for work owned by this group.</summary>
    public Guid? BusinessCalendarId { get; set; }

    public bool IsActive { get; set; } = true;

    public User? Manager { get; set; }
    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
}

public enum GroupType
{
    Assignment = 1,
    Approval = 2,
    Notification = 3,
    Security = 4
}

/// <summary>Membership of a user in a group.</summary>
public class GroupMember : TenantEntity
{
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Leads can reassign work inside the group without tenant-wide permissions.</summary>
    public bool IsLead { get; set; }

    public Group? Group { get; set; }
    public User? User { get; set; }
}
