using Azure.AI.OpenAI;
using Azure.Communication.Email;
using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Ai;
using NexaOps.Application.Identity;
using NexaOps.Application.Changes;
using NexaOps.Application.Assets;
using NexaOps.Application.Cmdb;
using NexaOps.Application.Incidents;
using NexaOps.Application.Knowledge;
using NexaOps.Application.Problems;
using NexaOps.Application.Requests;
using NexaOps.Application.Notifications;
using NexaOps.Application.Sla;
using NexaOps.Domain.Identity;
using NexaOps.Infrastructure.Ai;
using NexaOps.Infrastructure.Caching;
using NexaOps.Infrastructure.Common;
using NexaOps.Infrastructure.Identity;
using NexaOps.Infrastructure.Messaging;
using NexaOps.Infrastructure.Notifications;
using NexaOps.Infrastructure.Persistence;
using NexaOps.Infrastructure.Persistence.Interceptors;
using NexaOps.Infrastructure.Persistence.Repositories;
using NexaOps.Infrastructure.Storage;

namespace NexaOps.Infrastructure;

/// <summary>
/// Composes the infrastructure layer.
/// <para>
/// Every Azure client is optional. When a service is not configured the corresponding adapter
/// is registered in a mode that reports itself unavailable, so the whole product runs on a
/// laptop with no Azure subscription and no feature silently pretends to work.
/// </para>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddNexaOpsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<MessagingOptions>(configuration.GetSection(MessagingOptions.SectionName));
        services.Configure<AzureAiOptions>(configuration.GetSection(AzureAiOptions.SectionName));

        AddPersistence(services, configuration);
        AddPlatformServices(services);
        AddCaching(services, configuration);
        AddAzureClients(services, configuration);

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("NexaOpsDb")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:NexaOpsDb is not configured. In Azure this is an Entra " +
                "authentication connection string with no password.");

        services.AddScoped<AuditAndTenantInterceptor>();

        services.AddDbContext<NexaOpsDbContext>((provider, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(NexaOpsDbContext).Assembly.FullName);

                // Azure SQL throttles and fails over. Retrying transient faults is the
                // difference between a brief blip and a visible outage.
                sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);

                sql.CommandTimeout(60);
            });

            options.AddInterceptors(provider.GetRequiredService<AuditAndTenantInterceptor>());

            // Query splitting avoids the cartesian explosion that Include chains cause on a
            // record page with several collections.
            options.ConfigureWarnings(w =>
                w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning));
        });

        // A separate factory for work that must not join the request's transaction - the
        // immediate audit writer in particular, which has to survive a rollback.
        services.AddDbContextFactory<NexaOpsDbContext>((provider, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(NexaOpsDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
            });
        }, ServiceLifetime.Scoped);

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IncidentRepository>();
        services.AddScoped<IIncidentRepository>(p => p.GetRequiredService<IncidentRepository>());
        services.AddScoped<ISlaInstanceWriter, SlaInstanceWriter>();

        services.AddScoped<SlaRepository>();
        services.AddScoped<ISlaRepository>(p => p.GetRequiredService<SlaRepository>());
        services.AddScoped<ISlaRepositoryScheduleAccessor>(p => p.GetRequiredService<SlaRepository>());

        // Service catalogue, requests and approvals.
        services.AddScoped<IRequestRepository, RequestRepository>();
        services.AddScoped<ICatalogRepository, CatalogRepository>();
        services.AddScoped<IApprovalRepository, ApprovalRepository>();
        services.AddScoped<IRequestQueryService, RequestQueryService>();

        // Problem management.
        services.AddScoped<IProblemRepository, ProblemRepository>();
        services.AddScoped<IProblemQueryService, ProblemQueryService>();

        // Change management.
        services.AddScoped<IChangeRepository, ChangeRepository>();
        services.AddScoped<IChangeQueryService, ChangeQueryService>();

        // Knowledge base.
        services.AddScoped<IKnowledgeRepository, KnowledgeRepository>();
        services.AddScoped<IKnowledgeQueryService, KnowledgeQueryService>();

        // CMDB.
        services.AddScoped<ICmdbRepository, CmdbRepository>();
        services.AddScoped<ICmdbQueryService, CmdbQueryService>();

        // Asset management.
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<IAssetQueryService, AssetQueryService>();

        services.AddScoped<IIncidentQueryService, IncidentQueryService>();
        services.AddScoped<IServiceDeskReferenceRepository, ServiceDeskReferenceRepository>();
        services.AddScoped<INumberSequenceService, NumberSequenceService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
    }

    private static void AddPlatformServices(IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, SystemClock>();

        // The tenant scope is per-request (or per background job iteration), and the same
        // instance serves both the read interface and the setter.
        services.AddScoped<AmbientTenantContext>();
        services.AddScoped<ITenantContext>(p => p.GetRequiredService<AmbientTenantContext>());
        services.AddScoped<ITenantContextSetter>(p => p.GetRequiredService<AmbientTenantContext>());

        // The web host replaces both of these with HTTP-backed implementations. These
        // registrations serve background workers, the seeder, and tests.
        services.TryAddScopedDefault<ICurrentUser, SystemCurrentUser>();
        services.TryAddScopedDefault<ICorrelationContext, BackgroundCorrelationContext>();

        // PBKDF2, HMAC-SHA512, 210 000 iterations - the ASP.NET Core v3 format.
        services.AddSingleton<IPasswordHasher<User>>(_ =>
            new PasswordHasher<User>(Microsoft.Extensions.Options.Options.Create(
                new PasswordHasherOptions
                {
                    CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
                    IterationCount = 210_000
                })));

        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<INotificationService, NotificationService>();
    }

    private static void AddCaching(IServiceCollection services, IConfiguration configuration)
    {
        var redis = configuration.GetConnectionString("Redis");

        if (string.IsNullOrWhiteSpace(redis))
        {
            // A single-instance in-memory cache. Correct for local development; in Azure the
            // Redis connection string is always present, so this path is not a silent downgrade
            // of a multi-instance deployment.
            services.AddDistributedMemoryCache();
        }
        else
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redis;
                options.InstanceName = "nexaops:";
            });
        }

        services.AddScoped<IApplicationCache, ApplicationCache>();
    }

    /// <summary>
    /// Registers Azure SDK clients, each only when its configuration is present.
    /// <para>
    /// A client that is not configured is simply not registered, and the adapter that depends on
    /// it receives null through <c>GetService</c> and reports itself unavailable. Managed
    /// Identity is always preferred; a connection string is accepted only where the options class
    /// documents it as local-development-only.
    /// </para>
    /// </summary>
    private static void AddAzureClients(IServiceCollection services, IConfiguration configuration)
    {
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeInteractiveBrowserCredential = true
        });

        services.AddSingleton<TokenCredential>(credential);

        var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
                      ?? new StorageOptions();
        var email = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>()
                    ?? new EmailOptions();
        var messaging = configuration.GetSection(MessagingOptions.SectionName).Get<MessagingOptions>()
                        ?? new MessagingOptions();
        var ai = configuration.GetSection(AzureAiOptions.SectionName).Get<AzureAiOptions>()
                 ?? new AzureAiOptions();

        // --- Blob Storage ---
        if (!string.IsNullOrWhiteSpace(storage.BlobServiceUri))
        {
            services.AddSingleton(_ => new BlobServiceClient(new Uri(storage.BlobServiceUri), credential));
        }
        else if (!string.IsNullOrWhiteSpace(storage.ConnectionString))
        {
            services.AddSingleton(_ => new BlobServiceClient(storage.ConnectionString));
        }

        services.AddScoped<IFileStorage>(provider => new BlobFileStorage(
            provider.GetService<BlobServiceClient>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<ILogger<BlobFileStorage>>()));

        // --- Communication Services (email) ---
        if (!string.IsNullOrWhiteSpace(email.Endpoint))
        {
            services.AddSingleton(_ => new EmailClient(new Uri(email.Endpoint), credential));
        }
        else if (!string.IsNullOrWhiteSpace(email.ConnectionString))
        {
            services.AddSingleton(_ => new EmailClient(email.ConnectionString));
        }

        services.AddScoped<IEmailSender>(provider => new AzureEmailSender(
            provider.GetService<EmailClient>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailOptions>>(),
            provider.GetRequiredService<ILogger<AzureEmailSender>>()));

        // --- Service Bus ---
        if (!string.IsNullOrWhiteSpace(messaging.FullyQualifiedNamespace))
        {
            services.AddSingleton(_ => new ServiceBusClient(messaging.FullyQualifiedNamespace, credential));
        }
        else if (!string.IsNullOrWhiteSpace(messaging.ConnectionString))
        {
            services.AddSingleton(_ => new ServiceBusClient(messaging.ConnectionString));
        }

        services.AddScoped<IEventPublisher>(provider => new ServiceBusEventPublisher(
            provider.GetService<ServiceBusClient>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MessagingOptions>>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<ICorrelationContext>(),
            provider.GetRequiredService<ILogger<ServiceBusEventPublisher>>()));

        // --- Azure OpenAI ---
        if (!string.IsNullOrWhiteSpace(ai.Endpoint))
        {
            if (ai.UseManagedIdentity)
            {
                services.AddSingleton(_ => new AzureOpenAIClient(new Uri(ai.Endpoint), credential));
            }
            else if (!string.IsNullOrWhiteSpace(ai.ApiKey))
            {
                services.AddSingleton(_ => new AzureOpenAIClient(
                    new Uri(ai.Endpoint),
                    new System.ClientModel.ApiKeyCredential(ai.ApiKey)));
            }
        }

        services.AddScoped<IAiCompletionService>(provider =>
        {
            var client = provider.GetService<AzureOpenAIClient>();

            // No provider means AI reports itself unavailable. There is deliberately no
            // fallback implementation that fabricates answers.
            if (client is null || !ai.IsChatConfigured)
            {
                return new UnconfiguredAiCompletionService();
            }

            return new AzureOpenAiCompletionService(
                client,
                provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureAiOptions>>(),
                provider.GetRequiredService<ILogger<AzureOpenAiCompletionService>>());
        });
    }

    /// <summary>
    /// Registers a default implementation that a later registration (the web host) can replace.
    /// Keeps the infrastructure module usable standalone without fighting the host over
    /// HTTP-specific services.
    /// </summary>
    private static void TryAddScopedDefault<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        if (services.All(d => d.ServiceType != typeof(TService)))
        {
            services.AddScoped<TService, TImplementation>();
        }
    }
}
