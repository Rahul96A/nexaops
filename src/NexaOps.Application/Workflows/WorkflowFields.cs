using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Application.Workflows;

/// <summary>
/// The fields a rule may be written against, per module.
/// <para>
/// This is the published half of each module's <c>WorkflowFacts</c>: the entity decides what it
/// publishes, and this decides what a person is offered and what the validator accepts. Keeping
/// the two lists in step is checked by a test rather than by memory — a field offered here that
/// the entity does not publish would be a rule that silently never matches, which looks
/// configured and is not.
/// </para>
/// </summary>
public static class WorkflowFields
{
    private static readonly WorkflowFieldDto[] Incident =
    [
        new("Status", "Status", "New, Assigned, InProgress, Pending, Resolved, Closed, Cancelled"),
        new("Priority", "Priority", "1 is P1. \"More urgent than P2\" is therefore \"less than 2\"."),
        new("Impact", "Impact", "1 is the widest impact."),
        new("Urgency", "Urgency", "1 is the most urgent."),
        new("Channel", "Raised through", "Portal, Email, Phone, Chat, Walkup, Api, Monitoring"),
        new("Title", "Title", "Usually tested with \"contains\"."),
        new("CategoryId", "Category", null),
        new("CategoryName", "Category name", "Reads better in a rule; breaks if the category is renamed."),
        new("SubcategoryId", "Subcategory", null),
        new("AssignmentGroupId", "Assignment group", null),
        new("AssignmentGroupName", "Assignment group name", null),
        new("AssignedToUserId", "Assignee", "Test with \"is empty\" for unassigned records."),
        new("RequesterId", "Requester", null),
        new("OrganizationId", "Organisation", null),
        new("DepartmentId", "Department", null),
        new("IsMajorIncident", "Major incident", "true or false"),
        new("HasBreachedSla", "SLA breached", "true or false"),
        new("IsPriorityOverridden", "Priority overridden", "true or false"),
        new("ConfigurationItemId", "Configuration item", null),
        new("ReopenCount", "Times reopened", null)
    ];

    private static readonly WorkflowFieldDto[] Request =
    [
        new("Status", "Status", "Draft, Submitted, PendingApproval, Approved, Rejected, InProgress, Pending, Fulfilled, Closed, Cancelled"),
        new("Priority", "Priority", "1 is P1."),
        new("Channel", "Raised through", null),
        new("Title", "Title", null),
        new("CategoryId", "Category", null),
        new("CategoryName", "Category name", null),
        new("FulfilmentGroupId", "Fulfilment group", null),
        new("FulfilmentGroupName", "Fulfilment group name", null),
        new("AssignedToUserId", "Assignee", null),
        new("RequesterId", "Requester", null),
        new("RequestedForId", "Requested for", "Differs from the requester on proxy-raised requests."),
        new("OrganizationId", "Organisation", null),
        new("DepartmentId", "Department", null),
        new("HasBreachedSla", "SLA breached", "true or false"),
        new("TotalCost", "Total cost", "Indicative cost of the lines, in the tenant's currency.")
    ];

    private static readonly WorkflowFieldDto[] Problem =
    [
        new("Status", "Status", "New, Assigned, Investigating, KnownError, FixInProgress, Resolved, Closed, Cancelled"),
        new("Priority", "Priority", "1 is P1."),
        new("Origin", "Origin", "FromIncident, Proactive, Vendor, Audit"),
        new("Title", "Title", null),
        new("CategoryId", "Category", null),
        new("CategoryName", "Category name", null),
        new("SubcategoryId", "Subcategory", null),
        new("AssignmentGroupId", "Assignment group", null),
        new("AssignmentGroupName", "Assignment group name", null),
        new("AssignedToUserId", "Assignee", null),
        new("OwnerUserId", "Owner", "The person accountable for the investigation."),
        new("IsMajorProblem", "Major problem", "true or false")
    ];

    private static readonly WorkflowFieldDto[] Change =
    [
        new("Status", "Status", "Draft, Assessment, PendingApproval, Approved, Rejected, Scheduled, Implementing, Review, Closed, Cancelled"),
        new("Priority", "Priority", "1 is P1."),
        new("Risk", "Risk", "Low, Medium, High, VeryHigh"),
        new("Impact", "Impact", "1 is the widest impact."),
        new("Type", "Type", "Standard, Normal, Emergency"),
        new("Title", "Title", null),
        new("CategoryId", "Category", null),
        new("CategoryName", "Category name", null),
        new("AssignmentGroupId", "Assignment group", null),
        new("AssignmentGroupName", "Assignment group name", null),
        new("AssignedToUserId", "Assignee", null),
        new("RequestedByUserId", "Requested by", null),
        new("RequiresDowntime", "Requires downtime", "true or false"),
        new("ConfigurationItemId", "Configuration item", null)
    ];

    /// <summary>
    /// The fields for one module, or none.
    /// <para>
    /// A module with no entry is one whose records the engine is not driven from. Returning an
    /// empty list rather than throwing means the administration screen shows "no rules can be
    /// written here yet", which is the truth.
    /// </para>
    /// </summary>
    public static IReadOnlyList<WorkflowFieldDto> For(ServiceModule module) => module switch
    {
        ServiceModule.Incident => Incident,
        ServiceModule.Request => Request,
        ServiceModule.Problem => Problem,
        ServiceModule.Change => Change,
        _ => []
    };

    /// <summary>The modules that can carry rules today.</summary>
    public static IReadOnlyList<ServiceModule> SupportedModules =>
    [
        ServiceModule.Incident,
        ServiceModule.Request,
        ServiceModule.Problem,
        ServiceModule.Change
    ];
}
