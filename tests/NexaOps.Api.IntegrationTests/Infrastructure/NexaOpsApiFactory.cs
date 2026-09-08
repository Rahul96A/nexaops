using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexaOps.Infrastructure.Persistence;

namespace NexaOps.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API against a real SQL Server database for integration testing.
/// <para>
/// The database is deliberately SQL Server rather than an in-memory or SQLite substitute.
/// The properties these tests exist to prove - tenant query filters, the tenant guard on write,
/// <c>rowversion</c> concurrency, schema-qualified tables - are provider behaviour. Verifying
/// them against a different provider would be verifying the wrong thing.
/// </para>
/// <para>
/// Locally this uses LocalDB. In CI, point <c>NEXAOPS_TEST_SQL</c> at a SQL Server container.
/// Each run gets its own database, dropped when the run finishes.
/// </para>
/// </summary>
/// <remarks>
/// Deliberately not an <c>IAsyncLifetime</c>: <see cref="WebApplicationFactory{T}"/> already
/// implements <see cref="IAsyncDisposable"/> with a different signature, and having xunit own
/// the teardown as well produces two competing disposal paths. <see cref="TestEnvironment"/>
/// owns this object's lifetime and calls the two methods below explicitly.
/// </remarks>
public sealed class NexaOpsApiFactory : WebApplicationFactory<Program>
{
    private const string LocalDbTemplate =
        "Server=(localdb)\\MSSQLLocalDB;Database={0};Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>
    /// A signing key used only by the test host. It is random per run, so a token minted in one
    /// test run is worthless in another, and nothing here resembles a deployable default.
    /// </summary>
    private static readonly string TestSigningKey =
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));

    private readonly string _databaseName = $"NexaOpsTest_{Guid.NewGuid():N}";

    public string ConnectionString { get; private set; } = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var template = Environment.GetEnvironmentVariable("NEXAOPS_TEST_SQL");

        ConnectionString = string.IsNullOrWhiteSpace(template)
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, LocalDbTemplate, _databaseName)
            : template.Replace("{database}", _databaseName, StringComparison.Ordinal);

        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:NexaOpsDb", ConnectionString);
        builder.UseSetting("ConnectionStrings:Redis", string.Empty);
        builder.UseSetting("Auth:Mode", "Local");
        builder.UseSetting("Auth:SigningKey", TestSigningKey);
        builder.UseSetting("Auth:Issuer", "https://nexaops.test");
        builder.UseSetting("Auth:Audience", "nexaops-api");

        // The fixture migrates and seeds explicitly, so tests control the data they assert on.
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Demo:SeedOnStartup", "false");

        // No Azure services in tests. Each adapter reports itself unconfigured, which is exactly
        // the behaviour a customer without those services would get.
        builder.UseSetting("Storage:BlobServiceUri", string.Empty);
        builder.UseSetting("AzureAi:Endpoint", string.Empty);
        builder.UseSetting("Email:Endpoint", string.Empty);
        builder.UseSetting("Messaging:FullyQualifiedNamespace", string.Empty);
        builder.UseSetting("Azure:KeyVaultUri", string.Empty);

        // Every request in the suite arrives from the same address, so production rate limits
        // would throttle the tests rather than any real abuse. The limits themselves are
        // configuration and are asserted separately; here they are raised out of the way.
        builder.UseSetting("RateLimiting:GeneralRequestsPerMinute", "100000");
        builder.UseSetting("RateLimiting:AuthenticationAttempts", "10000");
        builder.UseSetting("RateLimiting:AiRequestsPerMinute", "10000");

        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

        builder.ConfigureTestServices(services =>
        {
            // The SLA monitor and the notification dispatcher would otherwise mutate the same
            // rows the tests are asserting on, mid-assertion. Their behaviour is covered by the
            // domain and application tests instead.
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
            {
                services.Remove(hosted);
            }
        });
    }

    /// <summary>Creates the test database by applying the real migrations.</summary>
    public async Task MigrateDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NexaOpsDbContext>();

        await context.Database.MigrateAsync();
    }

    /// <summary>Drops the test database. A failure here is reported but never fails the run.</summary>
    public async Task DropDatabaseAsync()
    {
        try
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<NexaOpsDbContext>();
            await context.Database.EnsureDeletedAsync();
        }
        catch (Exception)
        {
            // A test database left behind is untidy but must not fail the run.
        }
    }
}
