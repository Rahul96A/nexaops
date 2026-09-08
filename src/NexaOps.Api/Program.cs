using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Azure.Identity;
using FluentValidation;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using NexaOps.Api;
using NexaOps.Api.Authorization;
using NexaOps.Api.Configuration;
using NexaOps.Api.Identity;
using NexaOps.Api.Middleware;
using NexaOps.Api.Seeding;
using NexaOps.Api.Workers;
using NexaOps.Application;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Incidents;
using NexaOps.Infrastructure;
using NexaOps.Infrastructure.Identity;
using NexaOps.Infrastructure.Persistence;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------
// Configuration and secrets
// ---------------------------------------------------------------------
// Key Vault is layered over appsettings so that a secret referenced in configuration resolves
// from the vault at startup using the app's Managed Identity. No secret is ever committed.
var keyVaultUri = builder.Configuration["Azure:KeyVaultUri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUri),
        new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeInteractiveBrowserCredential = true
        }));
}

// ---------------------------------------------------------------------
// Logging
// ---------------------------------------------------------------------
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithProperty("Application", "NexaOps.Api"));

// ---------------------------------------------------------------------
// Application layers
// ---------------------------------------------------------------------
builder.Services.AddNexaOpsApplication();
builder.Services.AddNexaOpsInfrastructure(builder.Configuration);

// HTTP-backed identity replaces the background defaults registered by the infrastructure layer.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<ICorrelationContext, HttpCorrelationContext>();

// ---------------------------------------------------------------------
// Authentication
// ---------------------------------------------------------------------
var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>()
                  ?? new AuthOptions();

if (authOptions.Mode == AuthMode.Local)
{
    if (string.IsNullOrWhiteSpace(authOptions.SigningKey))
    {
        // Refusing to start is the correct behaviour. A generated or defaulted signing key would
        // let anyone who reads the source mint a valid token for any tenant.
        throw new InvalidOperationException(
            "Auth:SigningKey is not configured. Set it with 'dotnet user-secrets set \"Auth:SigningKey\" \"<32+ random bytes>\"' " +
            "for local development, or supply it from Azure Key Vault in a deployed environment.");
    }

    if (Encoding.UTF8.GetByteCount(authOptions.SigningKey) < 32)
    {
        throw new InvalidOperationException(
            "Auth:SigningKey must be at least 32 bytes to sign HMAC-SHA256 tokens safely.");
    }
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        if (authOptions.Mode == AuthMode.EntraId)
        {
            if (!authOptions.EntraId.IsConfigured)
            {
                throw new InvalidOperationException(
                    "Auth:Mode is EntraId but Auth:EntraId:TenantId and Auth:EntraId:ClientId are not configured.");
            }

            options.Authority = $"{authOptions.EntraId.Instance.TrimEnd('/')}/{authOptions.EntraId.TenantId}/v2.0";
            options.Audience = authOptions.EntraId.Audience ?? $"api://{authOptions.EntraId.ClientId}";

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        }
        else
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = authOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = authOptions.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(authOptions.SigningKey!)),

                // A generous skew would extend the life of a revoked token. Thirty seconds
                // covers real clock drift and nothing more.
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        }

        options.Events = new JwtBearerEvents
        {
            // The security stamp closes the window between a privilege change and token expiry.
            // Without it a token minted before a role was revoked would keep working for its
            // full lifetime.
            OnTokenValidated = async context =>
            {
                var stampClaim = context.Principal?.FindFirst(NexaOpsClaims.SecurityStamp)?.Value;
                var userIdClaim = context.Principal?.FindFirst(NexaOpsClaims.UserId)?.Value;

                if (string.IsNullOrWhiteSpace(stampClaim) || !Guid.TryParse(userIdClaim, out var userId))
                {
                    // Entra-issued tokens have no NexaOps stamp; they are validated by Entra and
                    // mapped to a local user elsewhere.
                    return;
                }

                var validator = context.HttpContext.RequestServices
                    .GetRequiredService<SecurityStampValidator>();

                if (!await validator.IsCurrentAsync(userId, stampClaim, context.HttpContext.RequestAborted))
                {
                    context.Fail("The security token is no longer valid. Sign in again.");
                }
            },

            OnChallenge = context =>
            {
                // Suppress the default empty 401 body so the client always receives Problem
                // Details, whatever the failure.
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";

                return context.Response.WriteAsync(
                    """
                    {"type":"https://docs.nexaops.io/problems/unauthenticated","title":"Authentication is required.","status":401,"code":"unauthenticated"}
                    """);
            }
        };
    });

builder.Services.AddScoped<SecurityStampValidator>();

// ---------------------------------------------------------------------
// Authorization
// ---------------------------------------------------------------------
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, PlatformAdministratorHandler>();

builder.Services.AddAuthorizationBuilder()
    // Every endpoint requires authentication unless it opts out with [AllowAnonymous].
    // Defaulting to closed means a new controller cannot be accidentally public.
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build())
    .AddPolicy("PlatformAdministrator", policy => policy
        .RequireAuthenticatedUser()
        .AddRequirements(new PlatformAdministratorRequirement()));

// ---------------------------------------------------------------------
// MVC, validation, JSON
// ---------------------------------------------------------------------
builder.Services
    .AddControllers(options =>
    {
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = false;
        options.Filters.Add<ValidationActionFilter>();
    })
    .AddJsonOptions(options =>
    {
        // Enums travel as names so the API is readable and a reordered enum cannot silently
        // change the meaning of a stored integer in a client.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    // Model-binding failures go through the same Problem Details path as everything else.
    options.SuppressModelStateInvalidFilter = false;
});

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.ReportApiVersions = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("X-Api-Version"));
})
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

// ---------------------------------------------------------------------
// OpenAPI
// ---------------------------------------------------------------------
builder.Services.AddOpenApi("v1", options =>
{
    // Declares the bearer scheme once on the document so every operation in the generated
    // client, and the Scalar UI, knows how to authenticate.
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "NexaOps API",
            Version = "v1",
            Description =
                "NexaOps - AI-native enterprise IT service management. " +
                "Every endpoint is tenant-scoped from the authenticated identity; a tenant " +
                "identifier supplied by a client is ignored."
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste the access token returned by POST /api/v1/auth/sign-in."
        };

        return Task.CompletedTask;
    });
});

// ---------------------------------------------------------------------
// CORS
// ---------------------------------------------------------------------
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options => options.AddPolicy("NexaOpsSpa", policy =>
{
    if (allowedOrigins.Length == 0)
    {
        // No wildcard fallback. An unconfigured CORS policy should block the browser, not open
        // the API to every origin on the internet.
        policy.WithOrigins("https://localhost:5173");
    }
    else
    {
        policy.WithOrigins(allowedOrigins);
    }

    policy.AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()
        .WithExposedHeaders(HttpCorrelationContext.HeaderName, "X-Api-Version");
}));

// ---------------------------------------------------------------------
// Rate limiting
// ---------------------------------------------------------------------
var rateLimits = builder.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
                 ?? new RateLimitOptions();

builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Per-user for authenticated traffic, per-IP otherwise, so one noisy tenant cannot exhaust
    // the budget of another and an unauthenticated flood is contained.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var partition = context.User.FindFirst(NexaOpsClaims.UserId)?.Value
                        ?? context.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous";

        return RateLimitPartition.GetTokenBucketLimiter(partition, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = rateLimits.GeneralRequestsPerMinute,
            TokensPerPeriod = rateLimits.GeneralRequestsPerMinute,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    // Sign-in is limited far more tightly, per source address, because it is the endpoint an
    // attacker will use to guess passwords.
    options.AddPolicy("authentication", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimits.AuthenticationAttempts,
                Window = TimeSpan.FromMinutes(rateLimits.AuthenticationWindowMinutes),
                QueueLimit = 0
            }));

    options.AddPolicy("ai", context =>
        RateLimitPartition.GetTokenBucketLimiter(
            context.User.FindFirst(NexaOpsClaims.UserId)?.Value ?? "anonymous",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = rateLimits.AiRequestsPerMinute,
                TokensPerPeriod = rateLimits.AiRequestsPerMinute,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

// ---------------------------------------------------------------------
// Observability
// ---------------------------------------------------------------------
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("NexaOps.Api", serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString())
        .AddAttributes([new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName)]))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation(options =>
        {
            // Health probes would otherwise dominate the trace volume and the bill.
            options.Filter = context => !context.Request.Path.StartsWithSegments("/health");
        })
        .AddHttpClientInstrumentation()
        // Statement text is deliberately NOT captured: query text can contain customer data,
        // and a trace backend is not the right place for it.
        .AddSqlClientInstrumentation())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(NexaOpsMetrics.MeterName));

var appInsightsConnection = builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(appInsightsConnection))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(options =>
        options.ConnectionString = appInsightsConnection);
}

builder.Services.AddSingleton<NexaOpsMetrics>();

// ---------------------------------------------------------------------
// Health checks
// ---------------------------------------------------------------------
builder.Services.AddHealthChecks()
    .AddDbContextCheck<NexaOpsDbContext>(
        name: "database",
        tags: ["ready"]);

// ---------------------------------------------------------------------
// Background workers and seeding
// ---------------------------------------------------------------------
builder.Services.AddScoped<TenantProvisioningService>();
builder.Services.AddScoped<DemoDataSeeder>();
builder.Services.AddHostedService<SlaMonitorWorker>();
builder.Services.AddHostedService<NotificationDispatchWorker>();

// Behind Front Door or App Service the client IP arrives in a forwarded header; without this
// rate limiting and audit would record the proxy's address for everyone.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// ---------------------------------------------------------------------
// Pipeline. Order matters.
// ---------------------------------------------------------------------
app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, elapsed, exception) =>
        httpContext.Request.Path.StartsWithSegments("/health")
            ? Serilog.Events.LogEventLevel.Verbose
            : exception is not null || httpContext.Response.StatusCode >= 500
                ? Serilog.Events.LogEventLevel.Error
                : Serilog.Events.LogEventLevel.Information;
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();

    app.MapScalarApiReference("/docs", options => options
        .WithTitle("NexaOps API")
        .WithOpenApiRoutePattern("/openapi/{documentName}.json"))
        .AllowAnonymous();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors("NexaOpsSpa");
app.UseRateLimiter();

app.UseAuthentication();

// Tenant scope is established after authentication and before authorization, so an authorization
// handler that touches the database is already correctly scoped.
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
}).AllowAnonymous();

// ---------------------------------------------------------------------
// Startup database work
// ---------------------------------------------------------------------
await using (var scope = app.Services.CreateAsyncScope())
{
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    if (configuration.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()))
    {
        // Convenient locally and in a demo environment. Production deploys run migrations as an
        // explicit pipeline step so that a schema change is reviewed and reversible, never a
        // side effect of an instance restarting.
        var context = scope.ServiceProvider.GetRequiredService<NexaOpsDbContext>();
        logger.LogInformation("Applying database migrations.");
        await context.Database.MigrateAsync();
    }

    if (configuration.GetValue("Demo:SeedOnStartup", false))
    {
        var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
        await seeder.SeedAsync(CancellationToken.None);
    }
}

await app.RunAsync();

/// <summary>
/// Exposed so the integration test host can reference the entry-point assembly.
/// </summary>
public partial class Program;
