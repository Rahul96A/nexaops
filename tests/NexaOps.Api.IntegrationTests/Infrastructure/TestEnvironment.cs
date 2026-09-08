using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexaOps.Api.Seeding;
using NexaOps.Application.Security;
using NexaOps.Domain.Identity;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Infrastructure.Persistence;

namespace NexaOps.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Two fully provisioned tenants with known users, shared by every integration test.
/// <para>
/// Two tenants rather than one is the point: the isolation tests need a real neighbour with real
/// records to try, and failing to reach them, before isolation can be said to hold.
/// </para>
/// </summary>
public sealed class TestEnvironment : IAsyncLifetime
{
    public const string Password = "IntegrationTest#2026";

    public NexaOpsApiFactory Factory { get; } = new();

    public TenantFixture Acme { get; private set; } = null!;
    public TenantFixture Northwind { get; private set; } = null!;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task InitializeAsync()
    {
        await Factory.MigrateDatabaseAsync();

        Acme = await ProvisionAsync("acme-test", "Acme Technologies India", "acmetest.example.in");
        Northwind = await ProvisionAsync("northwind-test", "Northwind Logistics India", "nwtest.example.in");
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }

        _clientLock.Dispose();
        await Factory.DropDatabaseAsync();
        await Factory.DisposeAsync();
    }

    /// <summary>Creates a tenant with an agent, a manager, an employee, and a category.</summary>
    private async Task<TenantFixture> ProvisionAsync(string code, string name, string domain)
    {
        using var scope = Factory.Services.CreateScope();
        var provisioning = scope.ServiceProvider.GetRequiredService<TenantProvisioningService>();
        var context = scope.ServiceProvider.GetRequiredService<NexaOpsDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        var tenant = await provisioning.ProvisionAsync(new TenantProvisioningRequest
        {
            Code = code,
            Name = name,
            PrimaryDomain = domain
        });

        // Provisioning runs outside a tenant scope by design; the rest of the fixture writes
        // rows for a tenant that does not yet have a request context, so it does the same.
        using var suppression = context.SuppressTenantFilter();
        using var auditSuppression = context.SuppressAuditCapture();

        var organization = new Organization
        {
            TenantId = tenant.Id,
            Code = "MAIN",
            Name = name,
            City = "Bengaluru",
            StateCode = "KA",
            CountryCode = "IN"
        };

        context.Organizations.Add(organization);

        var department = new Department
        {
            TenantId = tenant.Id,
            OrganizationId = organization.Id,
            Code = "IT",
            Name = "Information Technology"
        };

        context.Departments.Add(department);

        var group = new Group
        {
            TenantId = tenant.Id,
            Code = "SD",
            Name = "Service Desk",
            Type = GroupType.Assignment,
            IsActive = true
        };

        var otherGroup = new Group
        {
            TenantId = tenant.Id,
            Code = "NET",
            Name = "Network Operations",
            Type = GroupType.Assignment,
            IsActive = true
        };

        context.Groups.AddRange(group, otherGroup);

        var category = new Category
        {
            TenantId = tenant.Id,
            Code = "NETWORK",
            Name = "Network and connectivity",
            Module = ServiceModule.Incident,
            DefaultAssignmentGroupId = group.Id,
            IsActive = true
        };

        context.Categories.Add(category);

        var subcategory = new Subcategory
        {
            TenantId = tenant.Id,
            CategoryId = category.Id,
            Code = "VPN",
            Name = "VPN access",
            IsActive = true
        };

        context.Subcategories.Add(subcategory);
        await context.SaveChangesAsync();

        var roles = await context.Roles
            .Where(r => r.TenantId == tenant.Id)
            .ToDictionaryAsync(r => r.Code);

        var manager = CreateUser(context, hasher, tenant.Id, organization.Id, department.Id,
            "manager", "Priya", "Raghavan", domain);
        var agent = CreateUser(context, hasher, tenant.Id, organization.Id, department.Id,
            "agent", "Kavya", "Nair", domain);
        var employee = CreateUser(context, hasher, tenant.Id, organization.Id, department.Id,
            "employee", "Aditya", "Menon", domain);

        // Asset and licence administration is deliberately not held by the service desk manager,
        // so the fixture needs somebody who does hold it.
        var assetManager = CreateUser(context, hasher, tenant.Id, organization.Id, department.Id,
            "assetmanager", "Vikram", "Iyer", domain);

        await context.SaveChangesAsync();

        Assign(context, tenant.Id, manager.Id, roles[SystemRoles.ServiceDeskManager].Id);
        Assign(context, tenant.Id, agent.Id, roles[SystemRoles.ServiceDeskAgent].Id);
        Assign(context, tenant.Id, employee.Id, roles[SystemRoles.Requester].Id);
        Assign(context, tenant.Id, assetManager.Id, roles[SystemRoles.AssetManager].Id);

        foreach (var (userId, isLead) in new[] { (manager.Id, true), (agent.Id, false) })
        {
            context.GroupMembers.Add(new GroupMember
            {
                TenantId = tenant.Id,
                GroupId = group.Id,
                UserId = userId,
                IsLead = isLead
            });
        }

        group.ManagerUserId = manager.Id;
        await context.SaveChangesAsync();

        return new TenantFixture(
            tenant.Id,
            tenant.Code,
            organization.Id,
            department.Id,
            group.Id,
            otherGroup.Id,
            category.Id,
            subcategory.Id,
            new TestUser(manager.Id, manager.Email, "Priya Raghavan"),
            new TestUser(agent.Id, agent.Email, "Kavya Nair"),
            new TestUser(employee.Id, employee.Email, "Aditya Menon"),
            new TestUser(assetManager.Id, assetManager.Email, "Vikram Iyer"));
    }

    private static User CreateUser(
        NexaOpsDbContext context,
        IPasswordHasher<User> hasher,
        Guid tenantId,
        Guid organizationId,
        Guid departmentId,
        string alias,
        string firstName,
        string lastName,
        string domain)
    {
        var user = new User
        {
            TenantId = tenantId,
            Email = $"{alias}@{domain}",
            FirstName = firstName,
            LastName = lastName,
            DisplayName = $"{firstName} {lastName}",
            OrganizationId = organizationId,
            DepartmentId = departmentId,
            Status = UserStatus.Active
        };

        user.PasswordHash = hasher.HashPassword(user, Password);
        context.Users.Add(user);

        return user;
    }

    private static void Assign(NexaOpsDbContext context, Guid tenantId, Guid userId, Guid roleId)
        => context.UserRoles.Add(new UserRole { TenantId = tenantId, UserId = userId, RoleId = roleId });

    private readonly Dictionary<Guid, HttpClient> _clients = [];
    private readonly SemaphoreSlim _clientLock = new(1, 1);

    /// <summary>
    /// An authenticated client for this user, created once and reused.
    /// <para>
    /// Tests share the client rather than signing in repeatedly. That is not only faster: the
    /// sign-in endpoint is rate limited per source address, and every request in the suite comes
    /// from the same one, so re-authenticating per test throttles the suite against a control
    /// that is working exactly as intended.
    /// </para>
    /// </summary>
    public async Task<HttpClient> ClientForAsync(TestUser user)
    {
        await _clientLock.WaitAsync();
        try
        {
            if (_clients.TryGetValue(user.Id, out var existing))
            {
                return existing;
            }

            var client = await SignInAsync(user);
            _clients[user.Id] = client;
            return client;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    /// <summary>Signs in afresh and returns a client whose every request carries that token.</summary>
    public async Task<HttpClient> SignInAsync(TestUser user)
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/sign-in",
            new { email = user.Email, password = Password },
            Json);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SignInResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result!.AccessToken);

        return client;
    }

    /// <summary>An unauthenticated client, for testing that endpoints are closed by default.</summary>
    public HttpClient CreateAnonymousClient() => Factory.CreateClient();

    public sealed record SignInResponse(string AccessToken, string RefreshToken);
}

/// <param name="TenantId">Tenant identifier.</param>
/// <param name="Code">Tenant code.</param>
/// <param name="OrganizationId">The tenant's single organization.</param>
/// <param name="DepartmentId">The IT department.</param>
/// <param name="GroupId">Service Desk assignment group; the manager and agent belong to it.</param>
/// <param name="OtherGroupId">A group neither the manager nor the agent belongs to.</param>
/// <param name="CategoryId">Network category.</param>
/// <param name="SubcategoryId">VPN subcategory.</param>
/// <param name="Manager">Service desk manager.</param>
/// <param name="Agent">Service desk agent.</param>
/// <param name="Employee">An ordinary employee with the requester role only.</param>
/// <param name="AssetManager">Holds asset and licence administration.</param>
public sealed record TenantFixture(
    Guid TenantId,
    string Code,
    Guid OrganizationId,
    Guid DepartmentId,
    Guid GroupId,
    Guid OtherGroupId,
    Guid CategoryId,
    Guid SubcategoryId,
    TestUser Manager,
    TestUser Agent,
    TestUser Employee,

    /// <summary>Holds asset and licence administration, which the service desk manager does not.</summary>
    TestUser AssetManager);

/// <param name="Id">User identifier.</param>
/// <param name="Email">Sign-in address.</param>
/// <param name="DisplayName">Display name.</param>
public sealed record TestUser(Guid Id, string Email, string DisplayName);

/// <summary>
/// Shares one host and one database across the whole integration suite. Spinning up SQL Server
/// per test class would dominate the run time for no isolation benefit, because each test
/// creates the records it asserts on.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<TestEnvironment>
{
    public const string Name = "NexaOps integration";
}
