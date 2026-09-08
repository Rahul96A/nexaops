using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexaOps.Api.IntegrationTests.Infrastructure;
using NexaOps.Application.Common;
using NexaOps.Application.Incidents;
using NexaOps.Domain.Common;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Infrastructure.Persistence;

namespace NexaOps.Api.IntegrationTests;

/// <summary>
/// Tenant isolation.
/// <para>
/// This is the single most important guarantee a multi-tenant SaaS product makes, so it is
/// proved rather than assumed: every test here creates a real record in one tenant and then
/// tries, as a fully authenticated and highly privileged user of another tenant, to reach it.
/// </para>
/// <para>
/// Isolation is enforced at three independent layers - query filters, the write guard, and
/// explicit checks in application services - and these tests exercise all three, so a
/// regression in any one of them fails the build.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class TenantIsolationTests
{
    private readonly TestEnvironment _env;

    public TenantIsolationTests(TestEnvironment env) => _env = env;

    private async Task<IncidentDetailDto> CreateIncidentAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/incidents",
            new
            {
                title,
                description = "Raised by the integration suite to prove tenant isolation.",
                impact = "Moderate",
                urgency = "Medium"
            },
            TestEnvironment.Json);

        // Surface the problem details on failure: a bare status-code mismatch tells you nothing
        // about why the API refused, and these helpers run in every isolation test.
        if (response.StatusCode != HttpStatusCode.Created)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Expected 201 Created but got {(int)response.StatusCode}. Body: {body}");
        }

        return (await response.Content.ReadFromJsonAsync<IncidentDetailDto>(TestEnvironment.Json))!;
    }

    // -----------------------------------------------------------------
    // Reads
    // -----------------------------------------------------------------

    [Fact]
    public async Task A_user_cannot_read_an_incident_belonging_to_another_tenant()
    {
        var acmeClient = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await CreateIncidentAsync(acmeClient, "Acme VPN outage");

        var northwindClient = await _env.ClientForAsync(_env.Northwind.Manager);
        var response = await northwindClient.GetAsync($"/api/v1/incidents/{incident.Id}");

        // Reported as absent rather than forbidden: the existence of another tenant's record is
        // itself information we decline to confirm.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_user_cannot_read_another_tenants_incident_by_its_number()
    {
        var acmeClient = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await CreateIncidentAsync(acmeClient, "Acme ERP slowness");

        var northwindClient = await _env.ClientForAsync(_env.Northwind.Manager);
        var response = await northwindClient.GetAsync($"/api/v1/incidents/by-number/{incident.Number}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Search_never_returns_another_tenants_incidents()
    {
        var acmeClient = await _env.ClientForAsync(_env.Acme.Manager);
        await CreateIncidentAsync(acmeClient, "Acme-only marker incident");

        var northwindClient = await _env.ClientForAsync(_env.Northwind.Manager);

        var page = await northwindClient.GetFromJsonAsync<PagedResult<IncidentListItemDto>>(
            "/api/v1/incidents?search=Acme-only%20marker&pageSize=100",
            TestEnvironment.Json);

        page.ShouldNotBeNull();
        page.TotalCount.ShouldBe(0);
        page.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dashboard_counters_are_scoped_to_the_callers_tenant()
    {
        var acmeClient = await _env.ClientForAsync(_env.Acme.Manager);
        var northwindClient = await _env.ClientForAsync(_env.Northwind.Manager);

        var before = await northwindClient.GetFromJsonAsync<ServiceDeskSummaryDto>(
            "/api/v1/incidents/summary", TestEnvironment.Json);

        // Three new incidents in the neighbouring tenant.
        for (var i = 0; i < 3; i++)
        {
            await CreateIncidentAsync(acmeClient, $"Acme counter probe {i}");
        }

        var after = await northwindClient.GetFromJsonAsync<ServiceDeskSummaryDto>(
            "/api/v1/incidents/summary", TestEnvironment.Json);

        after!.OpenIncidents.ShouldBe(before!.OpenIncidents);
        after.CreatedToday.ShouldBe(before.CreatedToday);
    }

    [Fact]
    public async Task Comments_on_another_tenants_incident_are_not_readable()
    {
        var acmeClient = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await CreateIncidentAsync(acmeClient, "Acme incident with a comment");

        var comment = await acmeClient.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/comments",
            new { body = "Confidential Acme detail that Northwind must never see.", kind = "PublicComment" },
            TestEnvironment.Json);

        comment.StatusCode.ShouldBe(HttpStatusCode.Created);

        var northwindClient = await _env.ClientForAsync(_env.Northwind.Manager);
        var response = await northwindClient.GetAsync($"/api/v1/incidents/{incident.Id}/comments");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_activity_timeline_of_another_tenants_incident_is_not_readable()
    {
        var acmeClient = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await CreateIncidentAsync(acmeClient, "Acme incident with activity");

        var northwindClient = await _env.ClientForAsync(_env.Northwind.Manager);

        (await northwindClient.GetAsync($"/api/v1/incidents/{incident.Id}/activity"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reference_data_is_scoped_to_the_callers_tenant()
    {
        var acmeClient = await _env.ClientForAsync(_env.Acme.Manager);
        var northwindClient = await _env.ClientForAsync(_env.Northwind.Manager);

        var acmeGroups = await acmeClient.GetFromJsonAsync<List<GroupSummary>>(
            "/api/v1/reference/groups", TestEnvironment.Json);
        var northwindGroups = await northwindClient.GetFromJsonAsync<List<GroupSummary>>(
            "/api/v1/reference/groups", TestEnvironment.Json);

        acmeGroups.ShouldNotBeNull();
        northwindGroups.ShouldNotBeNull();

        // Both tenants have a group coded SD, but they are different rows entirely.
        acmeGroups.Select(g => g.Id).Intersect(northwindGroups.Select(g => g.Id)).ShouldBeEmpty();
        acmeGroups.ShouldContain(g => g.Id == _env.Acme.GroupId);
        northwindGroups.ShouldNotContain(g => g.Id == _env.Acme.GroupId);
    }

    [Fact]
    public async Task A_user_picker_cannot_be_used_to_enumerate_another_tenants_directory()
    {
        var northwindClient = await _env.ClientForAsync(_env.Northwind.Manager);

        // "Kavya" is an Acme agent. Northwind has an agent with the same first name, so the
        // search must return their own person and nobody else's.
        var users = await northwindClient.GetFromJsonAsync<List<UserSummary>>(
            "/api/v1/reference/users?search=Kavya", TestEnvironment.Json);

        users.ShouldNotBeNull();
        users.ShouldNotContain(u => u.Id == _env.Acme.Agent.Id);
    }

    // -----------------------------------------------------------------
    // Writes
    // -----------------------------------------------------------------

    [Fact]
    public async Task A_user_cannot_modify_an_incident_belonging_to_another_tenant()
    {
        var acmeClient = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await CreateIncidentAsync(acmeClient, "Acme incident that must not be edited");

        var northwindClient = await _env.ClientForAsync(_env.Northwind.Manager);

        var response = await northwindClient.PatchAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}",
            new { title = "Tampered by another tenant" },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And the record is genuinely untouched.
        var reread = await acmeClient.GetFromJsonAsync<IncidentDetailDto>(
            $"/api/v1/incidents/{incident.Id}", TestEnvironment.Json);

        reread!.Title.ShouldBe("Acme incident that must not be edited");
    }

    [Fact]
    public async Task A_user_cannot_comment_on_another_tenants_incident()
    {
        var acmeClient = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await CreateIncidentAsync(acmeClient, "Acme incident, no foreign comments");

        var northwindClient = await _env.ClientForAsync(_env.Northwind.Manager);

        var response = await northwindClient.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/comments",
            new { body = "This comment must never be written.", kind = "PublicComment" },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_user_cannot_resolve_another_tenants_incident()
    {
        var acmeClient = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await CreateIncidentAsync(acmeClient, "Acme incident, no foreign resolution");

        var northwindClient = await _env.ClientForAsync(_env.Northwind.Manager);

        var response = await northwindClient.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/status",
            new
            {
                status = "Resolved",
                resolutionCode = "Resolved",
                notes = "Closed by someone who should not be able to."
            },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_incident_cannot_be_assigned_to_a_group_from_another_tenant()
    {
        var acmeClient = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await CreateIncidentAsync(acmeClient, "Acme incident for cross-tenant assignment");

        var response = await acmeClient.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/assign",
            new { assignmentGroupId = _env.Northwind.GroupId },
            TestEnvironment.Json);

        // The group is invisible to this tenant, so it reads as a non-existent group.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_incident_cannot_be_assigned_to_a_user_from_another_tenant()
    {
        var acmeClient = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await CreateIncidentAsync(acmeClient, "Acme incident for cross-tenant assignee");

        var response = await acmeClient.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/assign",
            new { assignmentGroupId = _env.Acme.GroupId, assignedToUserId = _env.Northwind.Agent.Id },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_incident_cannot_be_classified_with_another_tenants_category()
    {
        var acmeClient = await _env.ClientForAsync(_env.Acme.Manager);

        var response = await acmeClient.PostAsJsonAsync(
            "/api/v1/incidents",
            new
            {
                title = "Incident with a foreign category",
                description = "The category belongs to another tenant and must be rejected.",
                impact = "Moderate",
                urgency = "Medium",
                categoryId = _env.Northwind.CategoryId
            },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // -----------------------------------------------------------------
    // The persistence layer itself
    // -----------------------------------------------------------------

    [Fact]
    public void Every_tenant_owned_entity_has_a_tenant_query_filter()
    {
        using var scope = _env.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NexaOpsDbContext>();

        var unfiltered = context.Model.GetEntityTypes()
            .Where(e => typeof(ITenantOwned).IsAssignableFrom(e.ClrType))
            .Where(e => e.BaseType is null)
            .Where(e => e.GetDeclaredQueryFilters() is null or { Count: 0 })
            .Select(e => e.ClrType.Name)
            .ToList();

        // The filter is applied by convention in OnModelCreating. This test is what stops a new
        // tenant-owned entity from quietly shipping without isolation if that convention ever
        // stops covering something.
        unfiltered.ShouldBeEmpty(
            $"These tenant-owned entities have no tenant query filter: {string.Join(", ", unfiltered)}");
    }

    [Fact]
    public async Task The_write_guard_refuses_to_persist_a_record_belonging_to_another_tenant()
    {
        using var scope = _env.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NexaOpsDbContext>();
        var tenantSetter = scope.ServiceProvider
            .GetRequiredService<NexaOps.Application.Abstractions.ITenantContextSetter>();

        // Scoped to Acme, but attempting to insert a Northwind row directly through the context -
        // bypassing every application service and controller.
        using var tenantScope = tenantSetter.BeginScope(
            _env.Acme.TenantId, _env.Acme.Code, "India Standard Time");

        context.Incidents.Add(new Incident
        {
            TenantId = _env.Northwind.TenantId,
            Number = $"INC-GUARD-{Guid.NewGuid():N}"[..18],
            Title = "Smuggled across a tenant boundary",
            Description = "This insert must be refused by the persistence guard.",
            RequesterId = _env.Northwind.Agent.Id
        });

        var error = await Should.ThrowAsync<TenantIsolationViolationException>(
            () => context.SaveChangesAsync());

        error.ExpectedTenantId.ShouldBe(_env.Acme.TenantId);
        error.ActualTenantId.ShouldBe(_env.Northwind.TenantId);
    }

    [Fact]
    public async Task The_write_guard_refuses_to_move_a_record_between_tenants()
    {
        var acmeClient = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await CreateIncidentAsync(acmeClient, "Acme incident that must stay put");

        using var scope = _env.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NexaOpsDbContext>();
        var tenantSetter = scope.ServiceProvider
            .GetRequiredService<NexaOps.Application.Abstractions.ITenantContextSetter>();

        using var tenantScope = tenantSetter.BeginScope(
            _env.Acme.TenantId, _env.Acme.Code, "India Standard Time");

        var tracked = await context.Incidents.FirstAsync(i => i.Id == incident.Id);

        // Re-tenanting an existing row is checked against the original value, so setting a
        // plausible-looking tenant on the way out cannot slip past the guard.
        tracked.TenantId = _env.Northwind.TenantId;

        await Should.ThrowAsync<TenantIsolationViolationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_query_with_no_tenant_scope_returns_nothing_rather_than_everything()
    {
        var acmeClient = await _env.ClientForAsync(_env.Acme.Manager);
        await CreateIncidentAsync(acmeClient, "An incident that exists");

        using var scope = _env.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NexaOpsDbContext>();

        // No tenant scope has been established on this context. Failing closed is the point:
        // an unscoped query must look like an empty result set, never like a full one.
        var incidents = await context.Incidents.ToListAsync();

        incidents.ShouldBeEmpty();
    }

    private sealed record GroupSummary(Guid Id, string Code, string Name);

    private sealed record UserSummary(Guid Id, string DisplayName, string Email);
}
