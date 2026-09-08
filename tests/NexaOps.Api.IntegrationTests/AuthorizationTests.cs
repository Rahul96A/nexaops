using System.Net;
using System.Net.Http.Json;
using NexaOps.Api.IntegrationTests.Infrastructure;
using NexaOps.Application.Common;
using NexaOps.Application.Identity;
using NexaOps.Application.Incidents;
using NexaOps.Application.Security;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Api.IntegrationTests;

/// <summary>
/// Authorization: what each role can and cannot do, enforced end to end over HTTP.
/// <para>
/// The product's authorization story is that enforcement is permission-based and happens twice -
/// once at the endpoint and again inside the application service. These tests exercise it the
/// way an attacker would: as a real, authenticated, lower-privileged user calling the endpoint
/// directly rather than through a UI that hides the button.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AuthorizationTests
{
    private readonly TestEnvironment _env;

    public AuthorizationTests(TestEnvironment env) => _env = env;

    private async Task<IncidentDetailDto> RaiseAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/incidents",
            new { title, description = "Raised by the authorization suite.", impact = "Moderate", urgency = "Medium" },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IncidentDetailDto>(TestEnvironment.Json))!;
    }

    // -----------------------------------------------------------------
    // Authentication is required by default
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("/api/v1/incidents")]
    [InlineData("/api/v1/incidents/summary")]
    [InlineData("/api/v1/reference/groups")]
    [InlineData("/api/v1/reference/categories")]
    [InlineData("/api/v1/notifications")]
    [InlineData("/api/v1/audit")]
    [InlineData("/api/v1/auth/me")]
    public async Task Endpoints_reject_anonymous_callers(string path)
    {
        // The host applies a fallback policy requiring authentication, so a new controller
        // cannot be accidentally public. This checks that policy is actually in force.
        var response = await _env.CreateAnonymousClient().GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_anonymous_rejection_is_returned_as_problem_details()
    {
        var response = await _env.CreateAnonymousClient().GetAsync("/api/v1/incidents");

        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("unauthenticated");
    }

    [Fact]
    public async Task A_forged_token_is_rejected()
    {
        var client = _env.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not.a.valid.token");

        (await client.GetAsync("/api/v1/incidents")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_endpoints_remain_open_for_probes()
    {
        var client = _env.CreateAnonymousClient();

        (await client.GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // -----------------------------------------------------------------
    // Permission enforcement
    // -----------------------------------------------------------------

    [Fact]
    public async Task An_employee_cannot_assign_an_incident()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await RaiseAsync(manager, "Incident an employee must not assign");

        var employee = await _env.ClientForAsync(_env.Acme.Employee);

        var response = await employee.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/assign",
            new { assignmentGroupId = _env.Acme.GroupId },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_employee_cannot_resolve_an_incident()
    {
        var employee = await _env.ClientForAsync(_env.Acme.Employee);
        var incident = await RaiseAsync(employee, "My own incident");

        // Even their own incident: raising a ticket does not confer the right to close it.
        var response = await employee.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/status",
            new { status = "Resolved", resolutionCode = "Resolved", notes = "I decided it is fine now." },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_agent_cannot_override_priority()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await RaiseAsync(manager, "Incident for a priority override attempt");

        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        var response = await agent.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/priority",
            new { overridePriority = "P1Critical", overrideReason = "The customer is shouting about it." },
            TestEnvironment.Json);

        // Departing from the impact/urgency matrix is a management decision, which is what makes
        // the audit trail for priority inflation worth reading.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_agent_can_still_reclassify_impact_and_urgency()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await RaiseAsync(manager, "Incident for a legitimate reclassification");

        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        var response = await agent.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/priority",
            new { impact = "Extensive", urgency = "Critical" },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<IncidentDetailDto>(TestEnvironment.Json);
        updated!.Priority.ShouldBe(Priority.P1Critical);
        updated.IsPriorityOverridden.ShouldBeFalse();
    }

    [Fact]
    public async Task An_agent_cannot_declare_a_major_incident()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await RaiseAsync(manager, "Incident for a major declaration attempt");

        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        var response = await agent.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/major",
            new { isMajorIncident = true, reason = "It feels quite serious to me." },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_agent_cannot_read_the_audit_trail()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        (await agent.GetAsync("/api/v1/audit")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_manager_can_read_the_audit_trail()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        (await manager.GetAsync("/api/v1/audit")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // -----------------------------------------------------------------
    // Record visibility
    // -----------------------------------------------------------------

    [Fact]
    public async Task An_employee_sees_only_incidents_they_are_involved_in()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var theirs = await RaiseAsync(manager, "An incident the employee has nothing to do with");

        var employee = await _env.ClientForAsync(_env.Acme.Employee);
        var mine = await RaiseAsync(employee, "An incident the employee raised themselves");

        var page = await employee.GetFromJsonAsync<PagedResult<IncidentListItemDto>>(
            "/api/v1/incidents?pageSize=200", TestEnvironment.Json);

        page.ShouldNotBeNull();
        page.Items.ShouldContain(i => i.Id == mine.Id);
        page.Items.ShouldNotContain(i => i.Id == theirs.Id);

        // And a direct request for the other incident reads as absent, not forbidden.
        (await employee.GetAsync($"/api/v1/incidents/{theirs.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_agent_sees_every_incident_in_their_tenant()
    {
        var employee = await _env.ClientForAsync(_env.Acme.Employee);
        var raised = await RaiseAsync(employee, "An incident raised by an employee");

        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        (await agent.GetAsync($"/api/v1/incidents/{raised.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // -----------------------------------------------------------------
    // Work notes
    // -----------------------------------------------------------------

    [Fact]
    public async Task An_employee_cannot_write_an_internal_work_note()
    {
        var employee = await _env.ClientForAsync(_env.Acme.Employee);
        var incident = await RaiseAsync(employee, "Incident for a work note attempt");

        var response = await employee.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/comments",
            new { body = "Trying to write an internal note.", kind = "WorkNote" },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Internal_work_notes_are_never_returned_to_a_requester()
    {
        var employee = await _env.ClientForAsync(_env.Acme.Employee);
        var incident = await RaiseAsync(employee, "Incident that will receive a work note");

        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        var note = await agent.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/comments",
            new { body = "Internal: the requester has done this three times this month.", kind = "WorkNote" },
            TestEnvironment.Json);

        note.StatusCode.ShouldBe(HttpStatusCode.Created);

        var reply = await agent.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/comments",
            new { body = "We are looking into it now.", kind = "PublicComment" },
            TestEnvironment.Json);

        reply.StatusCode.ShouldBe(HttpStatusCode.Created);

        // The requester sees the reply and not the note. The filtering happens in the query, so
        // the note never travels to a browser that must not see it.
        var comments = await employee.GetFromJsonAsync<List<IncidentCommentDto>>(
            $"/api/v1/incidents/{incident.Id}/comments", TestEnvironment.Json);

        comments.ShouldNotBeNull();
        comments.ShouldContain(c => c.Body.Contains("looking into it"));
        comments.ShouldNotContain(c => c.Kind == IncidentCommentKind.WorkNote);
        comments.ShouldNotContain(c => c.Body.Contains("three times this month"));

        // The agent sees both.
        var agentView = await agent.GetFromJsonAsync<List<IncidentCommentDto>>(
            $"/api/v1/incidents/{incident.Id}/comments", TestEnvironment.Json);

        agentView!.ShouldContain(c => c.Kind == IncidentCommentKind.WorkNote);
    }

    [Fact]
    public async Task The_comment_count_a_requester_sees_excludes_work_notes()
    {
        var employee = await _env.ClientForAsync(_env.Acme.Employee);
        var incident = await RaiseAsync(employee, "Incident for a comment count check");

        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        await agent.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/comments",
            new { body = "Internal note that must not be counted for the requester.", kind = "WorkNote" },
            TestEnvironment.Json);

        var asRequester = await employee.GetFromJsonAsync<IncidentDetailDto>(
            $"/api/v1/incidents/{incident.Id}", TestEnvironment.Json);

        var asAgent = await agent.GetFromJsonAsync<IncidentDetailDto>(
            $"/api/v1/incidents/{incident.Id}", TestEnvironment.Json);

        // A count that included hidden notes would leak their existence.
        asRequester!.CommentCount.ShouldBe(0);
        asAgent!.CommentCount.ShouldBe(1);
    }

    // -----------------------------------------------------------------
    // Profile and permissions
    // -----------------------------------------------------------------

    [Fact]
    public async Task The_profile_reports_the_permissions_the_role_actually_grants()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        var profile = await agent.GetFromJsonAsync<UserProfileDto>("/api/v1/auth/me", TestEnvironment.Json);

        profile.ShouldNotBeNull();
        profile.Roles.ShouldContain(SystemRoles.ServiceDeskAgent);

        profile.Permissions.ShouldContain(Permissions.IncidentReadAll);
        profile.Permissions.ShouldContain(Permissions.IncidentAssign);
        profile.Permissions.ShouldNotContain(Permissions.IncidentPriorityOverride);
        profile.Permissions.ShouldNotContain(Permissions.AuditRead);

        // The UI hides actions using this list, so it must match what the API enforces.
        profile.Permissions.ShouldAllBe(p => Permissions.IsKnown(p));
    }

    [Fact]
    public async Task The_profile_carries_the_tenant_and_its_localisation_settings()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var profile = await manager.GetFromJsonAsync<UserProfileDto>("/api/v1/auth/me", TestEnvironment.Json);

        profile!.TenantId.ShouldBe(_env.Acme.TenantId);
        profile.TenantCode.ShouldBe(_env.Acme.Code);
        profile.TimeZoneId.ShouldBe("India Standard Time");
        profile.Locale.ShouldBe("en-IN");
        profile.CurrencyCode.ShouldBe("INR");
        profile.DateFormat.ShouldBe("dd/MM/yyyy");
    }

    [Fact]
    public async Task A_platform_permission_is_held_by_nobody_in_a_customer_tenant()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var profile = await manager.GetFromJsonAsync<UserProfileDto>("/api/v1/auth/me", TestEnvironment.Json);

        profile!.IsPlatformAdministrator.ShouldBeFalse();
        profile.Permissions.ShouldNotContain(p => p.StartsWith("platform.", StringComparison.Ordinal));
    }
}
