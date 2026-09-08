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
        Permissions.UserRead,

        // Every employee can browse the catalogue and raise and track their own requests.
        Permissions.CatalogRead,
        Permissions.RequestRead,
        Permissions.RequestCreate,
        Permissions.RequestCommentCreate,

        Permissions.ProblemRead,

        // Knowing what is being changed to the services you depend on is not privileged.
        Permissions.ChangeRead,

        // The knowledge module only pays for itself if the people raising tickets can find the
        // answer before they do.
        Permissions.KnowledgeRead,
        Permissions.KnowledgeFeedback,

        // Granted to everyone because approval is scoped by the approval record itself, not by
        // this permission: holding it lets a user act on approvals addressed to them and on
        // nothing else. Line-manager approval means any employee may be an approver, so gating
        // it behind a role would strand requests whenever a manager lacked that role.
        Permissions.ApprovalAct
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
        Permissions.AiActionConfirm,

        // Request fulfilment is agent work, exactly as incident resolution is.
        Permissions.RequestReadAll,
        Permissions.RequestUpdate,
        Permissions.RequestAssign,
        Permissions.RequestFulfil,
        Permissions.RequestClose,
        Permissions.RequestWorkNoteRead,
        Permissions.RequestWorkNoteCreate,

        Permissions.ProblemCreate,
        Permissions.ProblemUpdate,
        Permissions.ProblemInvestigate,
        Permissions.ProblemCommentCreate,
        Permissions.ProblemWorkNoteRead,
        Permissions.ProblemLinkIncident,

        Permissions.ChangeCreate,
        Permissions.ChangeUpdate,
        Permissions.ChangeImplement,
        Permissions.ChangeCommentCreate,
        Permissions.ChangeWorkNoteRead,

        Permissions.KnowledgeReadInternal,
        Permissions.KnowledgeCreate,
        Permissions.KnowledgeUpdate
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
        Permissions.AuditRead,

        // Cancelling somebody else's request, editing the catalogue and seeing the whole
        // approval backlog are management acts, not agent acts.
        Permissions.RequestCancel,
        Permissions.CatalogManage,
        Permissions.ApprovalReadAll,

        // Publishing a known error commits the whole service desk to a workaround, and
        // resolving a problem asserts the cause is gone. Both are management acts.
        Permissions.ProblemAssign,
        Permissions.ProblemPublishKnownError,
        Permissions.ProblemResolve,
        Permissions.ProblemClose,
        Permissions.ProblemCancel,

        Permissions.ChangeAssign,
        Permissions.ChangeSchedule,
        Permissions.ChangeReview,
        Permissions.ChangeClose,
        Permissions.ChangeCancel,

        // Publishing puts an article in front of the whole organisation, so it is separate from
        // writing one: an author cannot self-publish unreviewed guidance.
        Permissions.KnowledgePublish,
        Permissions.KnowledgeRetire
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
                Permissions.AiAssistantUse,
                Permissions.RequestReadAll,
                Permissions.RequestWorkNoteRead,
                Permissions.ApprovalReadAll
            ]),

        new(ChangeManager,
            "Change Manager",
            "Owns the change process end to end: assessment, scheduling, the CAB, and post-implementation review. Also holds the emergency change authority.",
            [
                .. BaselinePermissions,
                Permissions.IncidentReadAll,
                Permissions.IncidentWorkNoteRead,
                Permissions.GroupRead,
                Permissions.SlaRead,
                Permissions.ReportView,
                Permissions.AiAssistantUse,
                Permissions.RequestReadAll,
                Permissions.RequestWorkNoteRead,
                Permissions.ApprovalReadAll,
                Permissions.ProblemCreate,
                Permissions.ProblemUpdate,
                Permissions.ProblemAssign,
                Permissions.ProblemInvestigate,
                Permissions.ProblemPublishKnownError,
                Permissions.ProblemResolve,
                Permissions.ProblemClose,
                Permissions.ProblemCommentCreate,
                Permissions.ProblemWorkNoteRead,
                Permissions.ProblemLinkIncident,

                Permissions.ChangeCreate,
                Permissions.ChangeUpdate,
                Permissions.ChangeAssign,
                Permissions.ChangeSchedule,
                Permissions.ChangeImplement,
                Permissions.ChangeReview,
                Permissions.ChangeClose,
                Permissions.ChangeCancel,
                Permissions.ChangeCommentCreate,
                Permissions.ChangeWorkNoteRead,

                // The control that stops the emergency path becoming the normal one.
                Permissions.ChangeRaiseEmergency
            ]),

        new(Approver,
            "Approver",
            "Authorises service requests routed to them, and can see the requests they are deciding on in full context.",
            [
                .. BaselinePermissions,
                Permissions.RequestReadAll,
                Permissions.RequestWorkNoteRead,
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
            "Owns the knowledge base: commissions articles, reviews them, publishes and retires them, and watches which ones actually help.",
            [
                .. BaselinePermissions,
                Permissions.IncidentReadAll,
                Permissions.ReportView,
                Permissions.AiAssistantUse,
                Permissions.KnowledgeReadInternal,
                Permissions.KnowledgeCreate,
                Permissions.KnowledgeUpdate,
                Permissions.KnowledgePublish,
                Permissions.KnowledgeRetire,
                Permissions.CategoryRead
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
