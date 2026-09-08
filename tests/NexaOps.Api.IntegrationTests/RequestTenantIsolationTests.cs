using System.Net;
using System.Net.Http.Json;
using NexaOps.Api.IntegrationTests.Infrastructure;
using NexaOps.Application.Common;
using NexaOps.Application.Requests;
using NexaOps.Domain.Approvals;
using NexaOps.Domain.Catalog;

namespace NexaOps.Api.IntegrationTests;

/// <summary>
/// Tenant isolation across the request, catalogue and approval module.
/// <para>
/// Every test runs as a fully authenticated, highly privileged user of a <em>neighbouring</em>
/// tenant against records that genuinely exist. A test that found nothing against an empty
/// neighbour would prove nothing.
/// </para>
/// <para>
/// These were written after a real miss: the demo seeder re-provisioned only the primary tenant
/// on an existing environment, so the neighbour never received newly added permissions and a
/// manual isolation check silently became a permission check - it returned 403 for the right
/// reason but the wrong one.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RequestTenantIsolationTests
{
    private readonly TestEnvironment _env;

    public RequestTenantIsolationTests(TestEnvironment env) => _env = env;

    [Fact]
    public async Task A_request_cannot_be_read_across_tenants_by_id_or_by_number()
    {
        var (request, _) = await AcmeRequestAsync();
        var neighbour = await _env.ClientForAsync(_env.Northwind.Manager);

        // 404, not 403. A 403 confirms the id is real, which lets an attacker enumerate another
        // customer's record numbers.
        (await neighbour.GetAsync($"/api/v1/requests/{request.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await neighbour.GetAsync($"/api/v1/requests/by-number/{request.Number}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Searching_never_returns_another_tenants_requests()
    {
        var (request, _) = await AcmeRequestAsync();
        var neighbour = await _env.ClientForAsync(_env.Northwind.Manager);

        var results = await neighbour.GetFromJsonAsync<PagedResult<RequestSummaryDto>>(
            $"/api/v1/requests?search={request.Number}", TestEnvironment.Json);

        results.ShouldNotBeNull();
        results.Items.ShouldBeEmpty();

        // The total must be zero too, not merely the page - a non-zero count would disclose that
        // matching records exist somewhere.
        results.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Dashboard_counters_are_tenant_scoped()
    {
        var neighbour = await _env.ClientForAsync(_env.Northwind.Manager);

        var before = await neighbour.GetFromJsonAsync<RequestSummaryCountsDto>(
            "/api/v1/requests/summary", TestEnvironment.Json);

        await AcmeRequestAsync();

        var after = await neighbour.GetFromJsonAsync<RequestSummaryCountsDto>(
            "/api/v1/requests/summary", TestEnvironment.Json);

        after!.OpenRequests.ShouldBe(before!.OpenRequests);
        after.CreatedToday.ShouldBe(before.CreatedToday);
    }

    [Fact]
    public async Task Comments_are_not_readable_across_tenants()
    {
        var (request, _) = await AcmeRequestAsync();
        var neighbour = await _env.ClientForAsync(_env.Northwind.Manager);

        (await neighbour.GetAsync($"/api/v1/requests/{request.Id}/comments"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_tenants_request_cannot_be_modified()
    {
        var (request, _) = await AcmeRequestAsync();
        var neighbour = await _env.ClientForAsync(_env.Northwind.Manager);

        (await neighbour.PostAsJsonAsync($"/api/v1/requests/{request.Id}/cancel",
                new CancelRequestCommand { Reason = "Interfering with a neighbour." }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await neighbour.PostAsJsonAsync($"/api/v1/requests/{request.Id}/comments",
                new AddRequestCommentCommand { Body = "Interfering." }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The record is verified untouched, so a refused write really did not land.
        var owner = await _env.ClientForAsync(_env.Acme.Employee);
        var reloaded = await owner.GetFromJsonAsync<RequestDetailDto>(
            $"/api/v1/requests/{request.Id}", TestEnvironment.Json);

        reloaded!.CancellationReason.ShouldBeNull();
    }

    [Fact]
    public async Task The_catalogue_is_tenant_scoped()
    {
        var acmeManager = await _env.ClientForAsync(_env.Acme.Manager);
        var item = await PublishAsync(acmeManager, "ISOLATED");

        var neighbour = await _env.ClientForAsync(_env.Northwind.Manager);

        (await neighbour.GetAsync($"/api/v1/catalog/items/{item.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var browse = await neighbour.GetFromJsonAsync<List<CatalogItemSummaryDto>>(
            "/api/v1/catalog/items", TestEnvironment.Json);

        browse!.ShouldNotContain(i => i.Id == item.Id);
    }

    [Fact]
    public async Task Another_tenants_catalogue_item_cannot_be_ordered()
    {
        var acmeManager = await _env.ClientForAsync(_env.Acme.Manager);
        var item = await PublishAsync(acmeManager, "NOT-YOURS");

        var neighbour = await _env.ClientForAsync(_env.Northwind.Employee);

        var response = await neighbour.PostAsJsonAsync("/api/v1/requests", new CreateRequestCommand
        {
            Items = [new RequestLineInput { CatalogItemId = item.Id, Quantity = 1 }]
        });

        // Reads as a catalogue item that does not exist, because the look-up is tenant-filtered.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_tenants_approval_cannot_be_decided_or_even_seen()
    {
        var (_, approvalId) = await AcmeRequestAsync(requiresApproval: true);
        approvalId.ShouldNotBeNull();

        var neighbour = await _env.ClientForAsync(_env.Northwind.Manager);

        (await neighbour.PostAsJsonAsync($"/api/v1/approvals/{approvalId}/decide",
                new DecideApprovalCommand { Approved = true }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var queue = await neighbour.GetFromJsonAsync<List<ApprovalDto>>(
            "/api/v1/approvals?outstandingOnly=false", TestEnvironment.Json);

        queue!.ShouldNotContain(a => a.Id == approvalId);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private async Task<(RequestDetailDto Request, Guid? ApprovalId)> AcmeRequestAsync(
        bool requiresApproval = false)
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var employee = await _env.ClientForAsync(_env.Acme.Employee);

        var item = await PublishAsync(
            manager,
            requiresApproval ? "ISO-APPROVAL" : "ISO-PLAIN",
            requiresApproval ? _env.Acme.Manager.Id : null);

        var response = await employee.PostAsJsonAsync("/api/v1/requests", new CreateRequestCommand
        {
            Items = [new RequestLineInput { CatalogItemId = item.Id, Quantity = 1 }]
        });

        response.EnsureSuccessStatusCode();

        var request = (await response.Content.ReadFromJsonAsync<RequestDetailDto>(TestEnvironment.Json))!;

        return (request, request.Approvals.FirstOrDefault()?.Id);
    }

    private async Task<CatalogItemDetailDto> PublishAsync(
        HttpClient client,
        string code,
        Guid? approverUserId = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/catalog/items", new UpsertCatalogItemCommand
        {
            Code = $"{code}-{Guid.NewGuid():N}"[..20],
            Name = $"Item {code}",
            ShortDescription = "Created by the isolation suite.",
            FulfilmentGroupId = _env.Acme.GroupId,
            RequiresApproval = approverUserId is not null,
            ApprovalTargetKind = ApprovalTargetKind.User,
            ApproverUserId = approverUserId,
            ApprovalRule = ApprovalRule.AnyOne,
            Variables = []
        });

        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CatalogItemDetailDto>(TestEnvironment.Json))!;

        var publish = await client.PostAsync($"/api/v1/catalog/items/{created.Id}/publish", null);
        publish.EnsureSuccessStatusCode();

        return (await publish.Content.ReadFromJsonAsync<CatalogItemDetailDto>(TestEnvironment.Json))!;
    }
}
