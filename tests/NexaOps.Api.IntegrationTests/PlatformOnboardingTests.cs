using System.Net;
using System.Net.Http.Json;
using NexaOps.Api.IntegrationTests.Infrastructure;
using NexaOps.Application.Common;
using NexaOps.Application.Platform;
using NexaOps.Domain.Identity;

namespace NexaOps.Api.IntegrationTests;

/// <summary>
/// Tenant onboarding end to end, over real HTTP against real SQL Server.
/// <para>
/// Two things are being protected here and they pull in opposite directions. The platform
/// operator must be able to reach across every tenant, because that is the job. Everybody else
/// must not, no matter how much authority they hold inside their own tenant — a tenant
/// administrator with every tenant permission there is still gets 403 from all of this.
/// </para>
/// <para>
/// The third is that a suspended tenant is actually suspended. A status column nothing enforces
/// would look identical in the database and be worth nothing.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class PlatformOnboardingTests
{
    private readonly TestEnvironment _env;

    public PlatformOnboardingTests(TestEnvironment env) => _env = env;

    // -----------------------------------------------------------------
    // The boundary
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("GET", "/api/v1/platform/tenants")]
    [InlineData("POST", "/api/v1/platform/tenants")]
    public async Task A_tenant_administrator_cannot_reach_platform_administration(string method, string path)
    {
        // The tenant administrator holds every permission in the product except the platform
        // ones. That is the entire distinction, so this is the test that says the distinction is
        // real rather than a naming convention.
        var client = await _env.ClientForAsync(_env.Acme.Administrator);

        var response = method == "GET"
            ? await client.GetAsync(path)
            : await client.PostAsJsonAsync(path, NewTenantCommand("forbidden-probe"), TestEnvironment.Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_reach_platform_administration()
    {
        var response = await _env.CreateAnonymousClient().GetAsync("/api/v1/platform/tenants");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_service_desk_manager_cannot_reach_platform_administration()
    {
        var client = await _env.ClientForAsync(_env.Acme.Manager);

        var response = await client.GetAsync("/api/v1/platform/tenants");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // -----------------------------------------------------------------
    // Onboarding
    // -----------------------------------------------------------------

    [Fact]
    public async Task Onboarding_creates_a_tenant_whose_administrator_can_sign_in_and_run_it()
    {
        // The whole point of the feature: after this call a customer exists and somebody can get
        // into it without a developer writing code.
        var platform = await _env.ClientForAsync(_env.Acme.PlatformOperator);

        var result = await OnboardAsync(platform, "zenith-in", "Zenith Manufacturing India");

        Assert.Equal("zenith-in", result.Tenant.Code);
        Assert.Equal(nameof(TenantStatus.Trial), result.Tenant.Status);
        Assert.Equal(1, result.Tenant.UserCount);
        Assert.Null(result.Tenant.LastSignInAt);

        Assert.False(string.IsNullOrWhiteSpace(result.TemporaryPassword));

        // The new administrator signs in with the one-time password and lands inside their own
        // tenant, holding tenant administration.
        var administrator = new TestUser(result.AdministratorUserId, result.AdministratorEmail, "Onboarded Admin");
        var client = await _env.SignInAsync(administrator, result.TemporaryPassword);

        var profile = await client.GetFromJsonAsync<ProfileResponse>("/api/v1/auth/me", TestEnvironment.Json);

        Assert.NotNull(profile);
        Assert.Equal(result.Tenant.Id, profile.TenantId);
        Assert.Equal("zenith-in", profile.TenantCode);

        // Their first obligation is to replace the password the operator knows.
        Assert.True(profile.MustChangePassword);

        // And they are emphatically not a platform administrator.
        Assert.False(profile.IsPlatformAdministrator);
        Assert.DoesNotContain("platform.tenant.manage", profile.Permissions);
    }

    [Fact]
    public async Task An_onboarded_tenant_is_provisioned_well_enough_to_use()
    {
        // A tenant with no roles, no priority matrix, no SLA policies and no number sequences
        // would still onboard "successfully" and then fail on the first incident. This asserts
        // the customer can actually do the job they bought the product for.
        var platform = await _env.ClientForAsync(_env.Acme.PlatformOperator);

        var result = await OnboardAsync(platform, "vertex-in", "Vertex Retail India");

        var administrator = new TestUser(result.AdministratorUserId, result.AdministratorEmail, "Onboarded Admin");
        var client = await _env.SignInAsync(administrator, result.TemporaryPassword);

        var roles = await client.GetFromJsonAsync<IReadOnlyList<RoleResponse>>(
            "/api/v1/admin/roles", TestEnvironment.Json);

        Assert.NotNull(roles);
        Assert.Contains(roles, r => r.Code == "tenant-administrator");
        Assert.Contains(roles, r => r.Code == "service-desk-agent");

        // An incident can be raised, which exercises the number sequence, the priority matrix
        // and the SLA policies in one call.
        var incident = await client.PostAsJsonAsync(
            "/api/v1/incidents",
            new
            {
                title = "First incident in a brand new tenant",
                description = "Raised by the integration suite to prove the tenant is usable.",
                impact = "Moderate",
                urgency = "Medium"
            },
            TestEnvironment.Json);

        Assert.Equal(HttpStatusCode.Created, incident.StatusCode);

        var created = await incident.Content.ReadFromJsonAsync<IncidentResponse>(TestEnvironment.Json);

        Assert.NotNull(created);
        Assert.StartsWith("INC", created.Number, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_onboarded_administrator_sees_nothing_belonging_to_another_tenant()
    {
        // Onboarding writes across the tenant boundary, which is the one place in the product
        // where a mistake would hand a new customer somebody else's data.
        var platform = await _env.ClientForAsync(_env.Acme.PlatformOperator);

        var result = await OnboardAsync(platform, "isolated-in", "Isolated Systems India");

        var administrator = new TestUser(result.AdministratorUserId, result.AdministratorEmail, "Onboarded Admin");
        var client = await _env.SignInAsync(administrator, result.TemporaryPassword);

        var users = await client.GetFromJsonAsync<PagedResult<UserResponse>>(
            "/api/v1/admin/users", TestEnvironment.Json);

        Assert.NotNull(users);

        // Exactly one user: themselves. Not Acme's five, and not Northwind's.
        Assert.Equal(1, users.TotalCount);
        Assert.Equal(result.AdministratorEmail, users.Items.Single().Email);

        var incidents = await client.GetFromJsonAsync<PagedResult<IncidentResponse>>(
            "/api/v1/incidents", TestEnvironment.Json);

        Assert.NotNull(incidents);
        Assert.Equal(0, incidents.TotalCount);
    }

    [Fact]
    public async Task An_onboarded_administrator_cannot_administer_the_platform()
    {
        var platform = await _env.ClientForAsync(_env.Acme.PlatformOperator);

        var result = await OnboardAsync(platform, "contained-in", "Contained Networks India");

        var administrator = new TestUser(result.AdministratorUserId, result.AdministratorEmail, "Onboarded Admin");
        var client = await _env.SignInAsync(administrator, result.TemporaryPassword);

        // Being handed the keys to a tenant must not be being handed the keys to the platform.
        var response = await client.GetAsync("/api/v1/platform/tenants");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_onboarded_tenant_appears_in_the_platform_list()
    {
        var platform = await _env.ClientForAsync(_env.Acme.PlatformOperator);

        var result = await OnboardAsync(platform, "listed-in", "Listed Industries India");

        var page = await platform.GetFromJsonAsync<PagedResult<TenantSummaryDto>>(
            "/api/v1/platform/tenants?search=listed-in", TestEnvironment.Json);

        Assert.NotNull(page);

        var listed = Assert.Single(page.Items);
        Assert.Equal(result.Tenant.Id, listed.Id);
        Assert.Equal(1, listed.UserCount);
    }

    // -----------------------------------------------------------------
    // Refusals
    // -----------------------------------------------------------------

    [Fact]
    public async Task A_duplicate_tenant_code_is_refused()
    {
        var platform = await _env.ClientForAsync(_env.Acme.PlatformOperator);

        await OnboardAsync(platform, "duplicate-in", "Duplicate Holdings India");

        var second = await platform.PostAsJsonAsync(
            "/api/v1/platform/tenants",
            NewTenantCommand("duplicate-in", administratorEmail: "someone.else@duplicate.example.in"),
            TestEnvironment.Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
    }

    [Theory]
    [InlineData("ab")]                 // too short
    [InlineData("Acme-IN")]            // upper case, which would break case-sensitive matching
    [InlineData("-leading")]           // cannot start with a hyphen
    [InlineData("trailing-")]          // cannot end with one
    [InlineData("double--hyphen")]     // reads as a typo and collides with slug conventions
    [InlineData("has space")]
    [InlineData("platform")]           // reserved
    [InlineData("api")]                // reserved
    public async Task An_unusable_tenant_code_is_refused(string code)
    {
        // The code is immutable and appears in URLs, so a bad one has to be caught here rather
        // than discovered by a customer.
        var platform = await _env.ClientForAsync(_env.Acme.PlatformOperator);

        var response = await platform.PostAsJsonAsync(
            "/api/v1/platform/tenants", NewTenantCommand(code), TestEnvironment.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_tenant_cannot_be_created_already_suspended()
    {
        var platform = await _env.ClientForAsync(_env.Acme.PlatformOperator);

        var command = NewTenantCommand("dead-on-arrival-in");
        command.Status = TenantStatus.Suspended;

        var response = await platform.PostAsJsonAsync(
            "/api/v1/platform/tenants", command, TestEnvironment.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -----------------------------------------------------------------
    // Suspension
    // -----------------------------------------------------------------

    [Fact]
    public async Task Suspending_a_tenant_stops_its_users_signing_in()
    {
        // Without enforcement at sign-in, "Suspended" is a label on a row. This is the test that
        // makes the status mean something.
        var platform = await _env.ClientForAsync(_env.Acme.PlatformOperator);

        var result = await OnboardAsync(platform, "suspended-in", "Suspended Traders India");
        var administrator = new TestUser(result.AdministratorUserId, result.AdministratorEmail, "Onboarded Admin");

        // Signs in perfectly well beforehand.
        var before = await _env.SignInAsync(administrator, result.TemporaryPassword);
        Assert.Equal(HttpStatusCode.OK, (await before.GetAsync("/api/v1/auth/me")).StatusCode);

        var suspend = await platform.PostAsJsonAsync(
            $"/api/v1/platform/tenants/{result.Tenant.Id}/status",
            new { status = nameof(TenantStatus.Suspended), reason = "Non-payment of the first invoice." },
            TestEnvironment.Json);

        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);

        var afterSignIn = await _env.CreateAnonymousClient().PostAsJsonAsync(
            "/api/v1/auth/sign-in",
            new { email = result.AdministratorEmail, password = result.TemporaryPassword },
            TestEnvironment.Json);

        Assert.Equal(HttpStatusCode.Unauthorized, afterSignIn.StatusCode);
    }

    [Fact]
    public async Task Suspending_a_tenant_revokes_its_refresh_tokens()
    {
        // Sign-in is not the only way back in. A refresh token that still works would let every
        // signed-in user of a suspended tenant carry on for its full lifetime.
        var platform = await _env.ClientForAsync(_env.Acme.PlatformOperator);

        var result = await OnboardAsync(platform, "refresh-in", "Refresh Logistics India");
        var administrator = new TestUser(result.AdministratorUserId, result.AdministratorEmail, "Onboarded Admin");

        var anonymous = _env.CreateAnonymousClient();

        var signIn = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/sign-in",
            new { email = result.AdministratorEmail, password = result.TemporaryPassword },
            TestEnvironment.Json);

        var tokens = await signIn.Content.ReadFromJsonAsync<TestEnvironment.SignInResponse>(TestEnvironment.Json);
        Assert.NotNull(tokens);

        await platform.PostAsJsonAsync(
            $"/api/v1/platform/tenants/{result.Tenant.Id}/status",
            new { status = nameof(TenantStatus.Suspended), reason = "Contract terminated." },
            TestEnvironment.Json);

        var refresh = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken = tokens.RefreshToken },
            TestEnvironment.Json);

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Reactivating_a_tenant_lets_its_users_back_in()
    {
        var platform = await _env.ClientForAsync(_env.Acme.PlatformOperator);

        var result = await OnboardAsync(platform, "restored-in", "Restored Foods India");

        await platform.PostAsJsonAsync(
            $"/api/v1/platform/tenants/{result.Tenant.Id}/status",
            new { status = nameof(TenantStatus.Suspended), reason = "Payment overdue." },
            TestEnvironment.Json);

        await platform.PostAsJsonAsync(
            $"/api/v1/platform/tenants/{result.Tenant.Id}/status",
            new { status = nameof(TenantStatus.Active), reason = (string?)null },
            TestEnvironment.Json);

        var signIn = await _env.CreateAnonymousClient().PostAsJsonAsync(
            "/api/v1/auth/sign-in",
            new { email = result.AdministratorEmail, password = result.TemporaryPassword },
            TestEnvironment.Json);

        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    [Fact]
    public async Task Suspension_requires_a_reason()
    {
        // Suspension is a commercial act. The audit trail has to answer "why is nobody able to
        // sign in" without a support call.
        var platform = await _env.ClientForAsync(_env.Acme.PlatformOperator);

        var result = await OnboardAsync(platform, "unexplained-in", "Unexplained Ventures India");

        var response = await platform.PostAsJsonAsync(
            $"/api/v1/platform/tenants/{result.Tenant.Id}/status",
            new { status = nameof(TenantStatus.Suspended), reason = (string?)null },
            TestEnvironment.Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task A_platform_operator_cannot_suspend_the_tenant_they_are_signed_in_to()
    {
        // There is no way back in from outside the product, so this would be unrecoverable.
        var platform = await _env.ClientForAsync(_env.Acme.PlatformOperator);

        var response = await platform.PostAsJsonAsync(
            $"/api/v1/platform/tenants/{_env.Acme.TenantId}/status",
            new { status = nameof(TenantStatus.Suspended), reason = "Testing the guard." },
            TestEnvironment.Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        // And Acme is still usable, which is the part that would actually hurt.
        var stillWorks = await (await _env.ClientForAsync(_env.Acme.Agent)).GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);
    }

    // -----------------------------------------------------------------
    // Settings
    // -----------------------------------------------------------------

    [Fact]
    public async Task Tenant_settings_can_be_changed_but_the_code_cannot()
    {
        var platform = await _env.ClientForAsync(_env.Acme.PlatformOperator);

        var result = await OnboardAsync(platform, "renamed-in", "Renamed Original India");

        var update = await platform.PutAsJsonAsync(
            $"/api/v1/platform/tenants/{result.Tenant.Id}",
            new
            {
                name = "Renamed Afterwards India",
                legalName = "Renamed Afterwards India Private Limited",
                primaryDomain = "renamed.example.in",
                recordRetentionDays = 1825,
                auditRetentionDays = 2555
            },
            TestEnvironment.Json);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var updated = await update.Content.ReadFromJsonAsync<TenantDetailDto>(TestEnvironment.Json);

        Assert.NotNull(updated);
        Assert.Equal("Renamed Afterwards India", updated.Name);
        Assert.Equal(1825, updated.RecordRetentionDays);

        // The code is not in the command at all, so it survives regardless of what was sent.
        Assert.Equal("renamed-in", updated.Code);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static CreateTenantCommand NewTenantCommand(
        string code,
        string name = "Integration Test Tenant",
        string? administratorEmail = null)
        => new()
        {
            Code = code,
            Name = name,
            LegalName = $"{name} Private Limited",
            PrimaryDomain = "example.in",
            Status = TenantStatus.Trial,
            AdministratorEmail = administratorEmail ?? $"admin@{code}.example.in",
            AdministratorFirstName = "Ananya",
            AdministratorLastName = "Krishnan",
            AdministratorJobTitle = "IT Director"
        };

    private static async Task<TenantOnboardingResult> OnboardAsync(
        HttpClient platform,
        string code,
        string name)
    {
        var response = await platform.PostAsJsonAsync(
            "/api/v1/platform/tenants", NewTenantCommand(code, name), TestEnvironment.Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<TenantOnboardingResult>(TestEnvironment.Json);

        Assert.NotNull(result);
        return result;
    }

    private sealed record ProfileResponse(
        Guid TenantId,
        string TenantCode,
        bool MustChangePassword,
        bool IsPlatformAdministrator,
        IReadOnlyList<string> Permissions);

    private sealed record RoleResponse(Guid Id, string Code, string Name);

    private sealed record UserResponse(Guid Id, string Email);

    private sealed record IncidentResponse(Guid Id, string Number);
}
