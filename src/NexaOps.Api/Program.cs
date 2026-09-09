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
    // A forwarding default rather than the bearer scheme directly.
    //
    // The tenant middleware runs immediately after UseAuthentication and reads the principal's
    // tenant claim, so whichever credential a caller presents has to have been authenticated by
    // then. With the bearer scheme as the default, a key-authenticated request reached that
    // middleware anonymous — its scheme only ran later, inside the authorization filter — and
    // arrived at the service with no tenant scope at all.
    .AddAuthentication(NexaOpsAuthentication.DefaultScheme)
    .AddPolicyScheme(NexaOpsAuthentication.DefaultScheme, NexaOpsAuthentication.DefaultScheme, options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.ContainsKey(NexaOpsAuthentication.KeyHeader)
                ? IntegrationKeyAuthenticationHandler.SchemeName
                : JwtBearerDefaults.AuthenticationScheme;
    })
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

// Machine callers. A second scheme rather than a bypass: an integration key produces the same
// shape of principal a person's token does, so the tenant middleware, the permission attributes
// and the query filters all apply to it without knowing it is not a person.
builder.Services
    .AddAuthentication()
    .AddScheme<IntegrationKeyOptions, IntegrationKeyAuthenticationHandler>(
        IntegrationKeyAuthenticationHandler.SchemeName,
        _ => { });

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

    // Inbound integration traffic, partitioned by key rather than by address: a mail provider
    // delivers from a pool of addresses, so limiting by IP would either throttle one tenant's
    // traffic because of another's or fail to limit anything at all.
    options.AddPolicy("integration", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Items.TryGetValue("IntegrationKeyId", out var keyId)
                ? keyId?.ToString() ?? "unknown"
                : "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimits.IntegrationRequestsPerMinute,
                Window = TimeSpan.FromMinutes(1),

                // A short queue rather than none: a provider delivering a burst after an outage
                // is normal traffic, and rejecting it outright would lose mail rather than
                // slowing it.
                QueueLimit = 20,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
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

// The single-page application, served from this same origin.
//
// Same origin is the whole point: the client calls /api/v1/... with relative paths, so there is
// no base URL to configure, no CORS policy to get wrong, and no second host to secure. The cost
// is that the API image carries the front end, which for one container is a fair trade.
//
// Only runs when the files are actually present. In development the front end is served by Vite
// with its own proxy, and this must not interfere.
var spaRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
var hasSpa = Directory.Exists(spaRoot) && File.Exists(Path.Combine(spaRoot, "index.html"));

if (hasSpa)
{
    app.UseDefaultFiles();

    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = context =>
        {
            var path = context.File.Name;

            // Vite writes a content hash into every asset file name, so a changed file is a
            // changed URL. Those are immutable and cached for a year. index.html is not
            // hashed -- it is the thing that points at the current hashes -- so it must never
            // be cached, or a deploy would be invisible until the browser felt like checking.
            context.Context.Response.Headers["Cache-Control"] =
                path.Equals("index.html", StringComparison.OrdinalIgnoreCase)
                    ? "no-cache, no-store, must-revalidate"
                    : "public, max-age=31536000, immutable";
        }
    });
}

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

if (hasSpa)
{
    // Client-side routing: a browser asking for /incidents/<id> must receive the application,
    // which then reads the path itself.
    //
    // Explicitly excluded are /api and /health. Without that exclusion an unmatched API route
    // would return index.html with a 200, so a caller with a typo in a URL would receive a page
    // of HTML where they expected a 404 -- and a client parsing JSON would fail with something
    // that looks nothing like the actual mistake.
    // Anonymous, necessarily. The fallback serves the application shell -- HTML and JavaScript
    // that contain no tenant data -- and it is what a browser receives when it asks for "/".
    // Behind the default authorize-everything policy it returned 401, which meant the sign-in
    // page could not be fetched without already being signed in: the application could not be
    // opened at all. Every byte of data it goes on to request is authorised as it always was.
    app.MapFallbackToFile("index.html").AllowAnonymous().Add(builder =>
    {
        var original = builder.RequestDelegate!;

        builder.RequestDelegate = async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api")
                || context.Request.Path.StartsWithSegments("/health"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await original(context);
        };
    });
}

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
