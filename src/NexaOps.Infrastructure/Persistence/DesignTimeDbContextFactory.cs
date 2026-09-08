using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NexaOps.Infrastructure.Identity;

namespace NexaOps.Infrastructure.Persistence;

/// <summary>
/// Builds a context for <c>dotnet ef</c> at design time.
/// <para>
/// Without this, the EF tools would boot the whole API host to find a context - which means
/// requiring a signing key, a Key Vault, and every other runtime prerequisite just to scaffold a
/// migration. This keeps schema work independent of runtime configuration.
/// </para>
/// <para>
/// The connection string here is only ever used to read the provider's SQL dialect while
/// generating migration code. It never opens a connection unless a developer explicitly runs
/// <c>database update</c>, and it is overridable with <c>NEXAOPS_MIGRATIONS_CONNECTION</c>.
/// </para>
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NexaOpsDbContext>
{
    private const string DefaultConnection =
        "Server=(localdb)\\MSSQLLocalDB;Database=NexaOps;Trusted_Connection=True;TrustServerCertificate=True";

    public NexaOpsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NEXAOPS_MIGRATIONS_CONNECTION")
                               ?? DefaultConnection;

        var options = new DbContextOptionsBuilder<NexaOpsDbContext>()
            .UseSqlServer(connectionString, sql =>
                sql.MigrationsAssembly(typeof(NexaOpsDbContext).Assembly.FullName))
            .Options;

        // At design time there is no request and no tenant. The no-tenant context reports
        // HasTenant false, which is all the model builder needs.
        return new NexaOpsDbContext(options, NoTenantContext.Instance);
    }
}
