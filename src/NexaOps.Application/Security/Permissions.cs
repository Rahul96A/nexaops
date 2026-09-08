namespace NexaOps.Application.Security;

/// <summary>
/// The complete catalogue of permissions NexaOps enforces.
/// <para>
/// This is the contract between the authorization system and the rest of the product. Every
/// protected operation names a constant from here; nothing anywhere branches on a role name.
/// A permission appears in this list only once it is actually enforced somewhere - the
/// catalogue is not a roadmap, it is a description of what the running system checks.
/// </para>
/// </summary>
public static class Permissions
{
    // ---------------------------------------------------------------
    // Platform - the operator of NexaOps itself, not a customer.
    // ---------------------------------------------------------------

    /// <summary>Read tenant records across the platform.</summary>
    public const string PlatformTenantRead = "platform.tenant.read";

    /// <summary>Create, configure and suspend tenants.</summary>
    public const string PlatformTenantManage = "platform.tenant.manage";

    // ---------------------------------------------------------------
    // Tenant administration
    // ---------------------------------------------------------------

    public const string OrganizationRead = "organization.read";
    public const string OrganizationManage = "organization.manage";
    public const string DepartmentRead = "department.read";
    public const string DepartmentManage = "department.manage";

    public const string UserRead = "user.read";
    public const string UserManage = "user.manage";

    /// <summary>Set another user's password or force a reset. Separated from general user editing.</summary>
    public const string UserResetPassword = "user.reset_password";

    public const string RoleRead = "role.read";

    /// <summary>Create roles and change their permission grants. Effectively privilege escalation.</summary>
    public const string RoleManage = "role.manage";

    public const string GroupRead = "group.read";
    public const string GroupManage = "group.manage";

    public const string CategoryRead = "category.read";
    public const string CategoryManage = "category.manage";

    public const string SettingRead = "setting.read";
    public const string SettingManage = "setting.manage";

    /// <summary>Read the audit trail. Deliberately not implied by any other permission.</summary>
    public const string AuditRead = "audit.read";

    // ---------------------------------------------------------------
    // SLA
    // ---------------------------------------------------------------

    public const string SlaRead = "sla.read";
    public const string SlaManage = "sla.manage";
    public const string CalendarRead = "calendar.read";
    public const string CalendarManage = "calendar.manage";

    // ---------------------------------------------------------------
    // Incident
    // ---------------------------------------------------------------

    /// <summary>
    /// Read incidents the caller is involved in - raised, affected by, assigned, or assigned to
    /// one of their groups. Every authenticated user holds this.
    /// </summary>
    public const string IncidentRead = "incident.read";

    /// <summary>Read every incident in the tenant. Agent-level and above.</summary>
    public const string IncidentReadAll = "incident.read.all";

    public const string IncidentCreate = "incident.create";
    public const string IncidentUpdate = "incident.update";
    public const string IncidentAssign = "incident.assign";
    public const string IncidentResolve = "incident.resolve";
    public const string IncidentClose = "incident.close";
    public const string IncidentReopen = "incident.reopen";
    public const string IncidentCancel = "incident.cancel";

    /// <summary>Depart from the impact/urgency matrix. Held by managers, not by every agent.</summary>
    public const string IncidentPriorityOverride = "incident.priority.override";

    /// <summary>Declare a major incident, which triggers escalation and comms.</summary>
    public const string IncidentDeclareMajor = "incident.declare_major";

    public const string IncidentCommentCreate = "incident.comment.create";

    /// <summary>See internal work notes. Requesters never hold this.</summary>
    public const string IncidentWorkNoteRead = "incident.worknote.read";
    public const string IncidentWorkNoteCreate = "incident.worknote.create";

    public const string IncidentAttachmentRead = "incident.attachment.read";
    public const string IncidentAttachmentUpload = "incident.attachment.upload";

    public const string IncidentExport = "incident.export";
    public const string IncidentArchive = "incident.archive";

    // ---------------------------------------------------------------
    // Service catalogue
    // ---------------------------------------------------------------

    /// <summary>Browse the catalogue and order from it. Every authenticated user holds this.</summary>
    public const string CatalogRead = "catalog.read";

    /// <summary>Create, edit, publish and retire catalogue items and their fields.</summary>
    public const string CatalogManage = "catalog.manage";

    // ---------------------------------------------------------------
    // Service request
    // ---------------------------------------------------------------

    /// <summary>
    /// Read requests the caller is involved in - raised, requested for, assigned, or in one of
    /// their fulfilment groups. Every authenticated user holds this.
    /// </summary>
    public const string RequestRead = "request.read";

    /// <summary>Read every request in the tenant. Fulfilment-agent level and above.</summary>
    public const string RequestReadAll = "request.read.all";

    public const string RequestCreate = "request.create";
    public const string RequestUpdate = "request.update";
    public const string RequestAssign = "request.assign";

    /// <summary>Mark request lines delivered and complete a request.</summary>
    public const string RequestFulfil = "request.fulfil";

    public const string RequestClose = "request.close";
    public const string RequestCancel = "request.cancel";
    public const string RequestCommentCreate = "request.comment.create";

    /// <summary>See internal work notes on requests. Requesters never hold this.</summary>
    public const string RequestWorkNoteRead = "request.worknote.read";
    public const string RequestWorkNoteCreate = "request.worknote.create";

    // ---------------------------------------------------------------
    // Approval
    // ---------------------------------------------------------------

    /// <summary>
    /// Act on approvals addressed to the caller. Holding this does not let anyone approve
    /// somebody else's approval - the record itself decides who may decide it.
    /// </summary>
    public const string ApprovalAct = "approval.act";

    /// <summary>See every approval in the tenant, not only one's own. A management view.</summary>
    public const string ApprovalReadAll = "approval.read.all";

    // ---------------------------------------------------------------
    // Reporting
    // ---------------------------------------------------------------

    public const string ReportView = "report.view";
    public const string ReportExport = "report.export";

    // ---------------------------------------------------------------
    // AI
    // ---------------------------------------------------------------

    /// <summary>Converse with the assistant at all.</summary>
    public const string AiAssistantUse = "ai.assistant.use";

    /// <summary>
    /// Confirm an AI-proposed change to a record. Holding this does not bypass the underlying
    /// permission: an AI-proposed assignment still requires <see cref="IncidentAssign"/>.
    /// </summary>
    public const string AiActionConfirm = "ai.action.confirm";

    /// <summary>Configure AI providers, prompts and enabled tools.</summary>
    public const string AiManage = "ai.manage";

    /// <summary>Metadata for every permission, used by the role administration UI.</summary>
    public static readonly IReadOnlyList<PermissionDescriptor> Catalogue =
    [
        new(PlatformTenantRead, "Platform", "View tenants", "Read tenant records across the platform."),
        new(PlatformTenantManage, "Platform", "Manage tenants", "Create, configure and suspend tenants."),

        new(OrganizationRead, "Organization", "View organizations", "Read organizations in the tenant."),
        new(OrganizationManage, "Organization", "Manage organizations", "Create and edit organizations."),
        new(DepartmentRead, "Organization", "View departments", "Read departments in the tenant."),
        new(DepartmentManage, "Organization", "Manage departments", "Create and edit departments."),

        new(UserRead, "Identity", "View users", "Read user profiles in the tenant."),
        new(UserManage, "Identity", "Manage users", "Create, edit, disable and archive users."),
        new(UserResetPassword, "Identity", "Reset passwords", "Set or force reset of another user's password."),
        new(RoleRead, "Identity", "View roles", "Read roles and their permission grants."),
        new(RoleManage, "Identity", "Manage roles", "Create roles and change permission grants. Grants privilege escalation."),
        new(GroupRead, "Identity", "View groups", "Read assignment and approval groups."),
        new(GroupManage, "Identity", "Manage groups", "Create and edit groups and their membership."),

        new(CategoryRead, "Configuration", "View categories", "Read the classification taxonomy."),
        new(CategoryManage, "Configuration", "Manage categories", "Edit categories, subcategories and the priority matrix."),
        new(SettingRead, "Configuration", "View settings", "Read tenant configuration values."),
        new(SettingManage, "Configuration", "Manage settings", "Change tenant configuration values."),
        new(AuditRead, "Security", "View audit trail", "Read the tenant audit log."),

        new(SlaRead, "SLA", "View SLAs", "Read SLA definitions and policies."),
        new(SlaManage, "SLA", "Manage SLAs", "Create and edit SLA definitions and policies."),
        new(CalendarRead, "SLA", "View business calendars", "Read working hours and holiday calendars."),
        new(CalendarManage, "SLA", "Manage business calendars", "Edit working hours and holidays."),

        new(IncidentRead, "Incident", "View own incidents", "Read incidents the user raised, is affected by, or is assigned."),
        new(IncidentReadAll, "Incident", "View all incidents", "Read every incident in the tenant."),
        new(IncidentCreate, "Incident", "Raise incidents", "Create new incidents."),
        new(IncidentUpdate, "Incident", "Edit incidents", "Change incident fields and classification."),
        new(IncidentAssign, "Incident", "Assign incidents", "Set the assignment group and assignee."),
        new(IncidentResolve, "Incident", "Resolve incidents", "Move an incident to Resolved with a resolution."),
        new(IncidentClose, "Incident", "Close incidents", "Close a resolved incident."),
        new(IncidentReopen, "Incident", "Reopen incidents", "Return a resolved incident to active work."),
        new(IncidentCancel, "Incident", "Cancel incidents", "Cancel an incident raised in error."),
        new(IncidentPriorityOverride, "Incident", "Override priority", "Set a priority that departs from the impact and urgency matrix."),
        new(IncidentDeclareMajor, "Incident", "Declare major incident", "Flag an incident as a major incident."),
        new(IncidentCommentCreate, "Incident", "Add comments", "Add customer-visible comments."),
        new(IncidentWorkNoteRead, "Incident", "View work notes", "Read internal work notes."),
        new(IncidentWorkNoteCreate, "Incident", "Add work notes", "Add internal work notes."),
        new(IncidentAttachmentRead, "Incident", "Download attachments", "Download files attached to incidents."),
        new(IncidentAttachmentUpload, "Incident", "Upload attachments", "Attach files to incidents."),
        new(IncidentExport, "Incident", "Export incidents", "Export incident lists to file."),
        new(IncidentArchive, "Incident", "Archive incidents", "Archive an incident record."),

        new(CatalogRead, "Catalogue", "Browse the catalogue", "View catalogue items and order from them."),
        new(CatalogManage, "Catalogue", "Manage the catalogue", "Create, edit, publish and retire catalogue items."),

        new(RequestRead, "Request", "View own requests", "Read requests the user raised, is the subject of, or is assigned."),
        new(RequestReadAll, "Request", "View all requests", "Read every service request in the tenant."),
        new(RequestCreate, "Request", "Raise requests", "Submit new service requests."),
        new(RequestUpdate, "Request", "Edit requests", "Change request fields and classification."),
        new(RequestAssign, "Request", "Assign requests", "Set the fulfilment group and assignee."),
        new(RequestFulfil, "Request", "Fulfil requests", "Mark request lines delivered and complete a request."),
        new(RequestClose, "Request", "Close requests", "Close a fulfilled request."),
        new(RequestCancel, "Request", "Cancel requests", "Cancel a request raised in error or no longer needed."),
        new(RequestCommentCreate, "Request", "Add comments", "Add requester-visible comments."),
        new(RequestWorkNoteRead, "Request", "View work notes", "Read internal work notes on requests."),
        new(RequestWorkNoteCreate, "Request", "Add work notes", "Add internal work notes on requests."),

        new(ApprovalAct, "Approval", "Act on approvals", "Approve or reject approvals addressed to the user."),
        new(ApprovalReadAll, "Approval", "View all approvals", "Read every approval in the tenant."),

        new(ReportView, "Reporting", "View reports", "Open dashboards and reports."),
        new(ReportExport, "Reporting", "Export reports", "Export report output to file."),

        new(AiAssistantUse, "AI", "Use AI assistant", "Ask the AI assistant questions grounded in tenant data."),
        new(AiActionConfirm, "AI", "Confirm AI actions", "Approve an AI-proposed change. Underlying permissions still apply."),
        new(AiManage, "AI", "Manage AI settings", "Configure AI providers, prompts and enabled tools.")
    ];

    private static readonly HashSet<string> Known =
        Catalogue.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// True when the code is a permission this build actually enforces. Role administration
    /// rejects unknown codes, so a typo cannot create a grant that silently never applies.
    /// </summary>
    public static bool IsKnown(string? code)
        => !string.IsNullOrWhiteSpace(code) && Known.Contains(code);

    public static IEnumerable<string> All => Known;
}

/// <summary>Human-readable metadata for one permission, surfaced in the role editor.</summary>
/// <param name="Code">Enforced identifier, e.g. <c>incident.assign</c>.</param>
/// <param name="Module">Grouping shown in the administration UI.</param>
/// <param name="Name">Short label.</param>
/// <param name="Description">What holding this permission lets someone do.</param>
public sealed record PermissionDescriptor(string Code, string Module, string Name, string Description);
