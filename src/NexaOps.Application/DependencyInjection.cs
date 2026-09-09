using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NexaOps.Application.Administration;
using NexaOps.Application.Ai;
using NexaOps.Application.Ai.Tools;
using NexaOps.Application.Changes;
using NexaOps.Application.Assets;
using NexaOps.Application.Cmdb;
using NexaOps.Application.Incidents;
using NexaOps.Application.Knowledge;
using NexaOps.Application.Problems;
using NexaOps.Application.Requests;
using NexaOps.Application.Sla;
using NexaOps.Application.Workflows;
using NexaOps.Application.Reporting;
using NexaOps.Application.Integration;

namespace NexaOps.Application;

/// <summary>Registers the application layer: use-case services, validators and AI tools.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddNexaOpsApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Use cases. Scoped, because they depend on the per-request tenant and user context.
        services.AddScoped<IIncidentService, IncidentService>();

        // Service catalogue, requests and approvals.
        services.AddScoped<IRequestService, RequestService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IApprovalService, ApprovalService>();

        // Problem management.
        services.AddScoped<IProblemService, ProblemService>();

        // Change management.
        services.AddScoped<IChangeService, ChangeService>();

        // Knowledge base.
        services.AddScoped<IKnowledgeService, KnowledgeService>();

        // CMDB.
        services.AddScoped<ICmdbService, CmdbService>();

        // Asset management.
        services.AddScoped<IAssetService, AssetService>();

        // Administration: the tenant's own configuration.
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IRoleAdminService, RoleAdminService>();
        services.AddScoped<ITaxonomyAdminService, TaxonomyAdminService>();

        // Integration: what arrives from outside, and the credentials that let it in.
        services.AddScoped<IInboundEmailService, InboundEmailService>();
        services.AddScoped<IIntegrationKeyService, IntegrationKeyService>();

        // Reporting.
        services.AddScoped<IReportService, ReportService>();

        // Workflow automation. The engine is scoped, and its recursion guard relies on that:
        // one instance per request is what stops a rule's own action re-entering the engine.
        services.AddScoped<IWorkflowService, WorkflowService>();
        services.AddScoped<IWorkflowEngine, WorkflowEngine>();
        services.AddScoped<ISlaService, SlaService>();

        // Validators are discovered by assembly scan so a new command validator is picked up
        // simply by existing, with no registration line to forget.
        services.AddValidatorsFromAssemblyContaining<CreateIncidentCommandValidator>(
            ServiceLifetime.Scoped,
            includeInternalTypes: false);

        // --- AI ---
        // Only read-only, permission-gated tools are registered. A tool that mutates state is
        // routed through an explicit human confirmation step rather than executed by the model.
        services.AddScoped<IAiTool, SearchIncidentsTool>();
        services.AddScoped<IAiTool, GetIncidentTool>();
        services.AddScoped<IAiTool, GetServiceDeskSummaryTool>();

        // Self-service tools, for the employee-facing virtual agent. Read-only like the rest:
        // the one thing the agent can cause to happen goes through a human confirmation.
        services.AddScoped<IAiTool, SearchKnowledgeTool>();
        services.AddScoped<IAiTool, GetMyRequestsTool>();
        services.AddScoped<IAiTool, SearchCatalogTool>();

        services.AddScoped<IAiToolRegistry, AiToolRegistry>();
        services.AddScoped<IAiToolExecutor, AiToolExecutor>();
        services.AddScoped<IVirtualAgentService, VirtualAgentService>();
        services.AddScoped<IAiAssistantService, AiAssistantService>();

        return services;
    }
}
