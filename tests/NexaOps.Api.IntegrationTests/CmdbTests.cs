using System.Net;
using System.Net.Http.Json;
using NexaOps.Api.IntegrationTests.Infrastructure;
using NexaOps.Application.Cmdb;
using NexaOps.Application.Common;
using NexaOps.Domain.Cmdb;

namespace NexaOps.Api.IntegrationTests;

/// <summary>
/// The CMDB end to end, over real HTTP against real SQL Server.
/// <para>
/// The behaviours worth protecting are that the dependency graph gives the right answer in the
/// right direction, that it cannot be made to span a tenant boundary, and that retiring an item
/// is a different permission from editing one.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class CmdbTests
{
    private readonly TestEnvironment _env;

    public CmdbTests(TestEnvironment env) => _env = env;

    [Fact]
    public async Task Two_items_cannot_share_a_name()
    {
        // People find a CI by the name they know - "sql-prod-01", not CI0000042 - so a duplicate
        // makes the CMDB ambiguous exactly where it is used most.
        var admin = await _env.ClientForAsync(_env.Acme.Manager);

        var name = $"dup-{Guid.NewGuid():N}"[..20];
        await CreateAsync(admin, name);

        var response = await admin.PostAsJsonAsync("/api/v1/cmdb/items", new UpsertCiCommand { Name = name });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Support_cannot_expire_before_the_item_was_acquired()
    {
        var admin = await _env.ClientForAsync(_env.Acme.Manager);

        var response = await admin.PostAsJsonAsync("/api/v1/cmdb/items", new UpsertCiCommand
        {
            Name = $"backwards-{Guid.NewGuid():N}"[..20],
            AcquiredOn = new DateOnly(2026, 6, 1),
            SupportExpiresOn = new DateOnly(2025, 6, 1)
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_item_cannot_depend_on_itself()
    {
        var admin = await _env.ClientForAsync(_env.Acme.Manager);
        var item = await CreateAsync(admin, $"self-{Guid.NewGuid():N}"[..20]);

        var response = await admin.PostAsJsonAsync($"/api/v1/cmdb/items/{item.Id}/relationships",
            new AddRelationshipCommand { TargetId = item.Id });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("cmdb.self_relationship");
    }

    [Fact]
    public async Task Impact_analysis_answers_what_breaks_and_what_it_relies_on()
    {
        var admin = await _env.ClientForAsync(_env.Acme.Manager);

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var server = await CreateAsync(admin, $"srv-{suffix}", CiType.Server);
        var database = await CreateAsync(admin, $"db-{suffix}", CiType.Database);
        var app = await CreateAsync(admin, $"app-{suffix}", CiType.Application);

        await LinkAsync(admin, database.Id, server.Id, CiRelationshipType.RunsOn);
        await LinkAsync(admin, app.Id, database.Id);

        var serverDetail = await admin.GetFromJsonAsync<CiDetailDto>(
            $"/api/v1/cmdb/items/{server.Id}", TestEnvironment.Json);

        // Breaking the server affects the database directly and the app transitively.
        serverDetail!.Impacts.Select(i => i.Id).ShouldContain(database.Id);
        serverDetail.Impacts.Select(i => i.Id).ShouldContain(app.Id);
        serverDetail.Impacts.Single(i => i.Id == database.Id).Depth.ShouldBe(1);
        serverDetail.Impacts.Single(i => i.Id == app.Id).Depth.ShouldBe(2);

        // And the server relies on nothing.
        serverDetail.DependsOn.ShouldBeEmpty();

        var appDetail = await admin.GetFromJsonAsync<CiDetailDto>(
            $"/api/v1/cmdb/items/{app.Id}", TestEnvironment.Json);

        // Direction is not symmetric.
        appDetail!.Impacts.ShouldBeEmpty();
        appDetail.DependsOn.Select(d => d.Id).ShouldContain(server.Id);
    }

    [Fact]
    public async Task Adding_the_same_relationship_twice_is_idempotent()
    {
        var admin = await _env.ClientForAsync(_env.Acme.Manager);

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var a = await CreateAsync(admin, $"a-{suffix}");
        var b = await CreateAsync(admin, $"b-{suffix}");

        await LinkAsync(admin, a.Id, b.Id);
        var second = await LinkAsync(admin, a.Id, b.Id);

        // A double-click must not double-count in impact analysis.
        second.DependsOn.Count(d => d.Id == b.Id).ShouldBe(1);
    }

    [Fact]
    public async Task A_relationship_can_be_removed()
    {
        var admin = await _env.ClientForAsync(_env.Acme.Manager);

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var a = await CreateAsync(admin, $"rm-a-{suffix}");
        var b = await CreateAsync(admin, $"rm-b-{suffix}");

        await LinkAsync(admin, a.Id, b.Id);

        var removed = await admin.DeleteAsync(
            $"/api/v1/cmdb/items/{a.Id}/relationships/{b.Id}?type=DependsOn");

        removed.EnsureSuccessStatusCode();

        var detail = await removed.Content.ReadFromJsonAsync<CiDetailDto>(TestEnvironment.Json);
        detail!.DependsOn.ShouldBeEmpty();
    }

    [Fact]
    public async Task Removing_a_relationship_that_does_not_exist_is_refused()
    {
        var admin = await _env.ClientForAsync(_env.Acme.Manager);

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var a = await CreateAsync(admin, $"none-a-{suffix}");
        var b = await CreateAsync(admin, $"none-b-{suffix}");

        var response = await admin.DeleteAsync(
            $"/api/v1/cmdb/items/{a.Id}/relationships/{b.Id}?type=DependsOn");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Out_of_support_is_judged_against_today()
    {
        var admin = await _env.ClientForAsync(_env.Acme.Manager);

        var expired = await CreateAsync(admin, $"old-{Guid.NewGuid():N}"[..20]);

        await admin.PutAsJsonAsync($"/api/v1/cmdb/items/{expired.Id}", new UpsertCiCommand
        {
            Name = expired.Name,
            SupportExpiresOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)
        });

        var detail = await admin.GetFromJsonAsync<CiDetailDto>(
            $"/api/v1/cmdb/items/{expired.Id}", TestEnvironment.Json);

        // An expired warranty discovered during an outage is the most expensive way to learn.
        detail!.IsOutOfSupport.ShouldBeTrue();
    }

    [Fact]
    public async Task An_agent_can_read_the_cmdb_but_not_edit_it()
    {
        // An agent who cannot see what a failing server supports cannot judge how urgent the
        // call is; that is different from letting them rewrite the estate.
        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        (await agent.GetAsync("/api/v1/cmdb/items")).EnsureSuccessStatusCode();

        var create = await agent.PostAsJsonAsync("/api/v1/cmdb/items",
            new UpsertCiCommand { Name = $"agent-{Guid.NewGuid():N}"[..20] });

        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_dependency_graph_cannot_be_made_to_span_a_tenant_boundary()
    {
        var acme = await _env.ClientForAsync(_env.Acme.Manager);
        var northwind = await _env.ClientForAsync(_env.Northwind.Manager);

        var mine = await CreateAsync(acme, $"mine-{Guid.NewGuid():N}"[..20]);
        var theirs = await CreateAsync(northwind, $"theirs-{Guid.NewGuid():N}"[..20]);

        var response = await acme.PostAsJsonAsync($"/api/v1/cmdb/items/{mine.Id}/relationships",
            new AddRelationshipCommand { TargetId = theirs.Id });

        // Reads as a non-existent item, because the look-up is tenant-filtered.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_tenants_item_is_not_reachable()
    {
        var acme = await _env.ClientForAsync(_env.Acme.Manager);
        var northwind = await _env.ClientForAsync(_env.Northwind.Manager);

        var item = await CreateAsync(acme, $"acme-only-{Guid.NewGuid():N}"[..20]);

        (await northwind.GetAsync($"/api/v1/cmdb/items/{item.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var search = await northwind.GetFromJsonAsync<PagedResult<CiSummaryDto>>(
            $"/api/v1/cmdb/items?search={item.Name}", TestEnvironment.Json);

        search!.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task An_unrecognised_sort_field_is_a_validation_failure()
    {
        var admin = await _env.ClientForAsync(_env.Acme.Manager);

        var response = await admin.GetAsync("/api/v1/cmdb/items?sortBy=; DROP TABLE ConfigurationItems");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static async Task<CiDetailDto> CreateAsync(
        HttpClient client,
        string name,
        CiType type = CiType.Server)
    {
        var response = await client.PostAsJsonAsync("/api/v1/cmdb/items", new UpsertCiCommand
        {
            Name = name,
            Type = type,
            Description = "Created by the integration suite.",
            Environment = "Test"
        });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CiDetailDto>(TestEnvironment.Json))!;
    }

    private static async Task<CiDetailDto> LinkAsync(
        HttpClient client,
        Guid sourceId,
        Guid targetId,
        CiRelationshipType type = CiRelationshipType.DependsOn)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/cmdb/items/{sourceId}/relationships",
            new AddRelationshipCommand { TargetId = targetId, Type = type });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CiDetailDto>(TestEnvironment.Json))!;
    }
}
