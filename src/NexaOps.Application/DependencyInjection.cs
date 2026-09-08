using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NexaOps.Application.Ai;
using NexaOps.Application.Ai.Tools;
using NexaOps.Application.Incidents;
using NexaOps.Application.Problems;
using NexaOps.Application.Requests;
using NexaOps.Application.Sla;

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

        services.AddScoped<IAiToolRegistry, AiToolRegistry>();
        services.AddScoped<IAiToolExecutor, AiToolExecutor>();
        services.AddScoped<IAiAssistantService, AiAssistantService>();

        return services;
    }
}
