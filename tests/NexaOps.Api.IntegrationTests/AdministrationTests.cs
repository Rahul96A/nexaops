using System.Net;
using System.Net.Http.Json;
using NexaOps.Api.IntegrationTests.Infrastructure;
using NexaOps.Application.Administration;
using NexaOps.Application.Common;
using NexaOps.Application.Security;
using NexaOps.Domain.Identity;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Api.IntegrationTests;

/// <summary>
/// Tenant administration end to end, over real HTTP against real SQL Server.
/// <para>
/// The behaviours worth protecting: revoking access takes effect on the next request rather than
/// at token expiry, a tenant cannot award itself platform permissions, configuration in use is
/// not deleted out from under the records that depend on it, and credential material never
/// appears in a response it should not.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AdministrationTests
{
    private readonly TestEnvironment _env;

    public AdministrationTests(TestEnvironment env) => _env = env;

    // -----------------------------------------------------------------
    // Users and access
    // -----------------------------------------------------------------

    [Fact]
    public async Task Revoking_a_role_ends_the_users_existing_sessions_immediately()
    {
        // The whole reason the security stamp exists. Without rotation on a role change, somebody
        // whose access was just removed keeps it until their token expires — which is exactly the
        // window that matters when access is removed because it should not have been held.
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);

        var created = await CreateUserAsync(admin, "stamp.rotation");

        var agentRole = (await RolesAsync(admin)).First(r => r.Code == SystemRoles.ServiceDeskAgent);

        await SetRolesAsync(admin, created.User.Id, [agentRole.Id]);

        // Sign in as them, and confirm the session works.
        var theirClient = await _env.SignInAsync(
            new TestUser(created.User.Id, created.User.Email, created.User.DisplayName),
            created.TemporaryPassword!);

        (await theirClient.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Now take the role away.
        await SetRolesAsync(admin, created.User.Id, []);

        // The token they still hold was issued under the old stamp, so the very next request
        // fails — not in fifteen minutes, now.
        (await theirClient.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Disabling_an_account_ends_its_sessions_immediately()
    {
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);

        var created = await CreateUserAsync(admin, "disable.now");

        var theirClient = await _env.SignInAsync(
            new TestUser(created.User.Id, created.User.Email, created.User.DisplayName),
            created.TemporaryPassword!);

        (await theirClient.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/users/{created.User.Id}/status",
            new { status = "Disabled" },
            TestEnvironment.Json);

        response.EnsureSuccessStatusCode();

        // An account disabled during an incident that keeps working for another hour is not
        // disabled.
        (await theirClient.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_administrator_cannot_disable_their_own_account()
    {
        // Recoverable only by somebody else, and in a tenant with one administrator there may be
        // nobody else.
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/users/{_env.Acme.Administrator.Id}/status",
            new { status = "Disabled" },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("user.cannot_disable_self");
    }

    [Fact]
    public async Task A_new_account_gets_one_password_that_is_never_returned_again()
    {
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);

        var created = await CreateUserAsync(admin, "one.time.password");

        created.TemporaryPassword.ShouldNotBeNullOrWhiteSpace();
        created.User.MustChangePassword.ShouldBeTrue();

        // Reading the user back must not carry it, and must not carry the hash or the stamp
        // either. A DTO is exactly where credential material leaks from.
        var response = await admin.GetAsync($"/api/v1/admin/users/{created.User.Id}");
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain(created.TemporaryPassword!);
        body.ShouldNotContain("passwordHash", Case.Insensitive);
        body.ShouldNotContain("securityStamp", Case.Insensitive);
    }

    [Fact]
    public async Task Two_people_cannot_share_a_sign_in_address()
    {
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);

        var email = $"duplicate.{Guid.NewGuid():N}@acmetest.example.in";

        await CreateUserAsync(admin, email: email);

        var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/users",
            new { email, firstName = "Second", lastName = "Person" },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_tenant_cannot_assign_a_role_from_another_tenant()
    {
        var acme = await _env.ClientForAsync(_env.Acme.Administrator);
        var northwind = await _env.ClientForAsync(_env.Northwind.Administrator);

        var theirRole = (await RolesAsync(northwind)).First(r => r.Code == SystemRoles.ServiceDeskAgent);

        var response = await acme.PutAsJsonAsync(
            $"/api/v1/admin/users/{_env.Acme.Employee.Id}/roles",
            new { roleIds = new[] { theirRole.Id } },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("user.unknown_role");
    }

    [Fact]
    public async Task Reading_the_directory_is_open_but_changing_it_is_not()
    {
        // Looking somebody up is baseline: an agent needs to find the person who raised a ticket.
        // Editing them, and granting them a role, are two further permissions beyond that.
        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        (await agent.GetAsync("/api/v1/admin/users")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var edit = await agent.PutAsJsonAsync(
            $"/api/v1/admin/users/{_env.Acme.Employee.Id}",
            new { email = _env.Acme.Employee.Email, firstName = "Aditya", lastName = "Menon" },
            TestEnvironment.Json);

        edit.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // And granting a role grants its permissions, which belongs with whoever is trusted to
        // define them rather than with whoever maintains job titles.
        var grant = await agent.PutAsJsonAsync(
            $"/api/v1/admin/users/{_env.Acme.Employee.Id}/roles",
            new { roleIds = Array.Empty<Guid>() },
            TestEnvironment.Json);

        grant.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The service desk manager runs the desk but does not administer the directory either.
        var deskManager = await _env.ClientForAsync(_env.Acme.Manager);

        var managerAttempt = await deskManager.PutAsJsonAsync(
            $"/api/v1/admin/users/{_env.Acme.Employee.Id}/roles",
            new { roleIds = Array.Empty<Guid>() },
            TestEnvironment.Json);

        managerAttempt.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // -----------------------------------------------------------------
    // Roles
    // -----------------------------------------------------------------

    [Fact]
    public async Task A_tenant_role_cannot_be_given_a_platform_permission()
    {
        // The boundary between administering a tenant and administering the platform.
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);

        var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/roles",
            new
            {
                name = $"Escalation attempt {Guid.NewGuid():N}"[..28],
                permissions = new[] { "platform.tenant.manage" }
            },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Platform permissions");
    }

    [Fact]
    public async Task The_permission_catalogue_withholds_platform_permissions()
    {
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);

        var permissions = await admin.GetFromJsonAsync<List<PermissionDto>>(
            "/api/v1/admin/permissions", TestEnvironment.Json);

        permissions.ShouldNotBeNull();
        permissions.ShouldNotBeEmpty();
        permissions.ShouldAllBe(p => !p.Code.StartsWith("platform."));
    }

    [Fact]
    public async Task A_permission_the_system_does_not_have_is_refused_rather_than_stored()
    {
        // Stored, it would be a grant that never matches anything — access apparently given and
        // none actually given.
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);

        var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/roles",
            new
            {
                name = $"Typo role {Guid.NewGuid():N}"[..24],
                permissions = new[] { "incident.reed" }
            },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("incident.reed");
    }

    [Fact]
    public async Task A_built_in_role_cannot_be_edited_or_deleted()
    {
        // The seeder maintains them, so an edit would be silently reverted on the next
        // provisioning run. Refused rather than accepted and lost.
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);

        var role = (await RolesAsync(admin)).First(r => r.IsSystem);

        var edit = await admin.PutAsJsonAsync(
            $"/api/v1/admin/roles/{role.Id}",
            new { name = "Renamed", permissions = role.Permissions },
            TestEnvironment.Json);

        edit.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var delete = await admin.DeleteAsync($"/api/v1/admin/roles/{role.Id}");
        delete.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await delete.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("role.system_immutable");
    }

    [Fact]
    public async Task A_role_somebody_holds_cannot_be_deleted()
    {
        // Deleting it would strip permissions from everybody holding it, and the audit trail
        // would record a role deletion rather than the access change each of them experienced.
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);

        var created = await admin.PostAsJsonAsync(
            "/api/v1/admin/roles",
            new
            {
                name = $"Temporary role {Guid.NewGuid():N}"[..28],
                permissions = new[] { Permissions.IncidentRead }
            },
            TestEnvironment.Json);

        created.EnsureSuccessStatusCode();
        var role = (await created.Content.ReadFromJsonAsync<RoleDetailDto>(TestEnvironment.Json))!;

        var user = await CreateUserAsync(admin, "role.holder");
        await SetRolesAsync(admin, user.User.Id, [role.Id]);

        var response = await admin.DeleteAsync($"/api/v1/admin/roles/{role.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("role.in_use");

        // Free it, and now it goes.
        await SetRolesAsync(admin, user.User.Id, []);
        (await admin.DeleteAsync($"/api/v1/admin/roles/{role.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // -----------------------------------------------------------------
    // Taxonomy
    // -----------------------------------------------------------------

    [Fact]
    public async Task A_category_records_classify_to_cannot_be_deleted()
    {
        // Deleting it would strip the classification from records that have one and silently
        // change reporting for the period they were raised in.
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);

        // Raised here rather than relying on another test class having filed one first: a test
        // whose outcome depends on execution order is not a test.
        var incident = await admin.PostAsJsonAsync(
            "/api/v1/incidents",
            new
            {
                title = "Incident holding a category open",
                description = "Raised by the administration suite so the category is in use.",
                impact = "Moderate",
                urgency = "Medium",
                categoryId = _env.Acme.CategoryId
            },
            TestEnvironment.Json);

        incident.StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await admin.DeleteAsync($"/api/v1/admin/categories/{_env.Acme.CategoryId}");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("category.in_use");

        // And the message says what to do instead, because "no" without an alternative is how
        // people end up editing the database by hand.
        problem.Detail.ShouldNotBeNull();
        problem.Detail.ShouldContain("Deactivate");
    }

    [Fact]
    public async Task An_unused_category_can_be_created_and_removed()
    {
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);

        var name = $"Scratch {Guid.NewGuid():N}"[..20];

        var created = await admin.PostAsJsonAsync(
            "/api/v1/admin/categories",
            new { name, module = "Incident" },
            TestEnvironment.Json);

        created.EnsureSuccessStatusCode();
        var category = (await created.Content.ReadFromJsonAsync<CategoryDto>(TestEnvironment.Json))!;

        category.RecordCount.ShouldBe(0);
        category.Code.ShouldNotBeNullOrWhiteSpace();

        (await admin.DeleteAsync($"/api/v1/admin/categories/{category.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_category_cannot_be_moved_between_modules()
    {
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);

        var created = await admin.PostAsJsonAsync(
            "/api/v1/admin/categories",
            new { name = $"Movable {Guid.NewGuid():N}"[..20], module = "Incident" },
            TestEnvironment.Json);

        created.EnsureSuccessStatusCode();
        var category = (await created.Content.ReadFromJsonAsync<CategoryDto>(TestEnvironment.Json))!;

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/admin/categories/{category.Id}",
            new { name = category.Name, module = "Change" },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("category.module_immutable");
    }

    [Fact]
    public async Task Group_membership_can_be_managed_and_leads_are_distinguished()
    {
        var admin = await _env.ClientForAsync(_env.Acme.Administrator);

        var created = await admin.PostAsJsonAsync(
            "/api/v1/admin/groups",
            new { name = $"Team {Guid.NewGuid():N}"[..16], type = "Assignment" },
            TestEnvironment.Json);

        created.EnsureSuccessStatusCode();
        var group = (await created.Content.ReadFromJsonAsync<GroupAdminDto>(TestEnvironment.Json))!;

        var withMember = await admin.PutAsJsonAsync(
            $"/api/v1/admin/groups/{group.Id}/members",
            new { userId = _env.Acme.Agent.Id, isLead = true },
            TestEnvironment.Json);

        withMember.EnsureSuccessStatusCode();
        var updated = (await withMember.Content.ReadFromJsonAsync<GroupAdminDto>(TestEnvironment.Json))!;

        var member = updated.Members.ShouldHaveSingleItem();
        member.UserId.ShouldBe(_env.Acme.Agent.Id);
        member.IsLead.ShouldBeTrue();

        // Setting the same person again changes the lead flag rather than adding them twice.
        var demoted = await admin.PutAsJsonAsync(
            $"/api/v1/admin/groups/{group.Id}/members",
            new { userId = _env.Acme.Agent.Id, isLead = false },
            TestEnvironment.Json);

        var afterDemotion = (await demoted.Content.ReadFromJsonAsync<GroupAdminDto>(TestEnvironment.Json))!;
        afterDemotion.Members.ShouldHaveSingleItem().IsLead.ShouldBeFalse();

        var removed = await admin.DeleteAsync(
            $"/api/v1/admin/groups/{group.Id}/members/{_env.Acme.Agent.Id}");

        removed.EnsureSuccessStatusCode();
        (await removed.Content.ReadFromJsonAsync<GroupAdminDto>(TestEnvironment.Json))!
            .Members.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_group_cannot_be_pointed_at_somebody_from_another_tenant()
    {
        var acme = await _env.ClientForAsync(_env.Acme.Administrator);

        var response = await acme.PostAsJsonAsync(
            "/api/v1/admin/groups",
            new
            {
                name = $"Cross {Guid.NewGuid():N}"[..16],
                type = "Assignment",
                managerUserId = _env.Northwind.Manager.Id
            },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task One_tenants_administration_cannot_see_another_tenants_directory()
    {
        var acme = await _env.ClientForAsync(_env.Acme.Administrator);

        (await acme.GetAsync($"/api/v1/admin/users/{_env.Northwind.Agent.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var users = await acme.GetFromJsonAsync<PagedResult<UserListItemDto>>(
            $"/api/v1/admin/users?search={_env.Northwind.Agent.Email}", TestEnvironment.Json);

        users!.Items.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private async Task<CreatedUserDto> CreateUserAsync(
        HttpClient client,
        string? prefix = null,
        string? email = null)
    {
        var address = email ?? $"{prefix}.{Guid.NewGuid():N}@acmetest.example.in";

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/users",
            new
            {
                email = address,
                firstName = "Test",
                lastName = "Account",
                jobTitle = "Created by the administration suite"
            },
            TestEnvironment.Json);

        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Expected 201 but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<CreatedUserDto>(TestEnvironment.Json))!;
    }

    private static async Task<IReadOnlyList<RoleDetailDto>> RolesAsync(HttpClient client)
        => (await client.GetFromJsonAsync<List<RoleDetailDto>>("/api/v1/admin/roles", TestEnvironment.Json))!;

    private static async Task SetRolesAsync(HttpClient client, Guid userId, Guid[] roleIds)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/v1/admin/users/{userId}/roles",
            new { roleIds },
            TestEnvironment.Json);

        response.EnsureSuccessStatusCode();
    }
}
