namespace NexaOps.Application.Security;

/// <summary>
/// The roles provisioned with every new tenant, and the permissions each one grants.
/// <para>
/// Roles are a convenience layer over <see cref="Permissions"/>. A customer may edit these
/// grants or define entirely new roles; nothing in the codebase depends on a role code.
/// </para>
/// <para>
/// Some roles (knowledge manager, asset manager, CMDB administrator, change manager) exist here
/// with a deliberately narrow grant because their modules ship in later phases. They are seeded
/// now so that a customer's user-to-role mapping does not have to be redone later, but they are
/// only granted permissions this build genuinely enforces - an unimplemented module does not get
/// a fake permission.
/// </para>
/// </summary>
public static class SystemRoles
{
    public const string PlatformAdministrator = "platform-administrator";
    public const string TenantAdministrator = "tenant-administrator";
    public const string ServiceDeskAgent = "service-desk-agent";
    public const string ServiceDeskManager = "service-desk-manager";
    public const string ItManager = "it-manager";
    public const string ChangeManager = "change-manager";
    public const string Approver = "approver";
    public const string Requester = "requester";
    public const string AssetManager = "asset-manager";
    public const string CmdbAdministrator = "cmdb-administrator";
    public const string KnowledgeManager = "knowledge-manager";
    public const string ReportViewer = "report-viewer";

    /// <summary>Every authenticated user receives these, regardless of role.</summary>
    private static readonly string[] BaselinePermissions =
    [
        Permissions.IncidentRead,
        Permissions.IncidentCreate,
        Permissions.IncidentCommentCreate,
        Permissions.IncidentAttachmentRead,
        Permissions.IncidentAttachmentUpload,
        Permissions.CategoryRead,
        Permissions.UserRead
    ];

    private static readonly string[] AgentPermissions =
    [
        .. BaselinePermissions,
        Permissions.IncidentReadAll,
        Permissions.IncidentUpdate,
        Permissions.IncidentAssign,
        Permissions.IncidentResolve,
        Permissions.IncidentClose,
        Permissions.IncidentWorkNoteRead,
        Permissions.IncidentWorkNoteCreate,
        Permissions.IncidentExport,
        Permissions.GroupRead,
        Permissions.OrganizationRead,
        Permissions.DepartmentRead,
        Permissions.SlaRead,
        Permissions.CalendarRead,
        Permissions.ReportView,
        Permissions.AiAssistantUse,
        Permissions.AiActionConfirm
    ];

    private static readonly string[] ServiceDeskManagerPermissions =
    [
        .. AgentPermissions,
        Permissions.IncidentReopen,
        Permissions.IncidentCancel,
        Permissions.IncidentPriorityOverride,
        Permissions.IncidentDeclareMajor,
        Permissions.IncidentArchive,
        Permissions.ReportExport,
        Permissions.GroupManage,
        Permissions.CategoryManage,
        Permissions.SlaManage,
        Permissions.CalendarManage,
        Permissions.AuditRead
    ];

    /// <summary>The seed definition of one role.</summary>
    /// <param name="Code">Stable role identifier.</param>
    /// <param name="Name">Display name.</param>
    /// <param name="Description">What the role is for.</param>
    /// <param name="Permissions">Permission codes granted.</param>
    /// <param name="IsPlatformScoped">True only for the platform operator role.</param>
    public sealed record Definition(
        string Code,
        string Name,
        string Description,
        IReadOnlyList<string> Permissions,
        bool IsPlatformScoped = false);

    /// <summary>All roles seeded into a new tenant.</summary>
    public static readonly IReadOnlyList<Definition> All =
    [
        new(PlatformAdministrator,
            "Platform Administrator",
            "Operates the NexaOps platform itself. Can manage tenants. Held by the service provider, not by customers.",
            [
                .. Security.Permissions.All
            ],
            IsPlatformScoped: true),

        new(TenantAdministrator,
            "Tenant Administrator",
            "Full administrative control within one tenant: users, roles, groups, configuration, SLAs and audit.",
            [
                .. Security.Permissions.All.Where(p => !p.StartsWith("platform.", StringComparison.Ordinal))
            ]),

        new(ServiceDeskAgent,
            "Service Desk Agent",
            "Works the incident queue: triages, assigns, investigates and resolves.",
            AgentPermissions),

        new(ServiceDeskManager,
            "Service Desk Manager",
            "Runs the service desk. Everything an agent can do, plus priority overrides, major incidents, SLA configuration and audit.",
            ServiceDeskManagerPermissions),

        new(ItManager,
            "IT Manager",
            "Oversight across IT operations with full read access and reporting, without day-to-day queue administration.",
            [
                .. BaselinePermissions,
                Permissions.IncidentReadAll,
                Permissions.IncidentWorkNoteRead,
                Permissions.IncidentExport,
                Permissions.IncidentPriorityOverride,
                Permissions.GroupRead,
                Permissions.OrganizationRead,
                Permissions.DepartmentRead,
                Permissions.SlaRead,
                Permissions.CalendarRead,
                Permissions.ReportView,
                Permissions.ReportExport,
                Permissions.AuditRead,
                Permissions.AiAssistantUse
            ]),

        new(ChangeManager,
            "Change Manager",
            "Owns the change process. Change management ships in a later phase; today this role carries service desk read access and reporting.",
            [
                .. BaselinePermissions,
                Permissions.IncidentReadAll,
                Permissions.IncidentWorkNoteRead,
                Permissions.GroupRead,
                Permissions.SlaRead,
                Permissions.ReportView,
                Permissions.AiAssistantUse
            ]),

        new(Approver,
            "Approver",
            "Approves requests and changes routed to them. Approval workflows ship with request management.",
            [
                .. BaselinePermissions,
                Permissions.ReportView
            ]),

        new(Requester,
            "Requester",
            "An employee raising and tracking their own tickets. The default role for every new user.",
            BaselinePermissions),

        new(AssetManager,
            "Asset Manager",
            "Owns hardware and software assets. Asset management ships in a later phase.",
            [
                .. BaselinePermissions,
                Permissions.IncidentReadAll,
                Permissions.ReportView,
                Permissions.OrganizationRead,
                Permissions.DepartmentRead
            ]),

        new(CmdbAdministrator,
            "CMDB Administrator",
            "Owns the configuration management database. The CMDB ships in a later phase.",
            [
                .. BaselinePermissions,
                Permissions.IncidentReadAll,
                Permissions.ReportView
            ]),

        new(KnowledgeManager,
            "Knowledge Manager",
            "Owns the knowledge base lifecycle. Knowledge management ships in a later phase.",
            [
                .. BaselinePermissions,
                Permissions.IncidentReadAll,
                Permissions.ReportView,
                Permissions.AiAssistantUse
            ]),

        new(ReportViewer,
            "Report Viewer",
            "Read-only access to dashboards and reports.",
            [
                .. BaselinePermissions,
                Permissions.IncidentReadAll,
                Permissions.ReportView,
                Permissions.ReportExport
            ])
    ];

    public static Definition? Find(string code)
        => All.FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase));
}
