using System.Net;
using System.Net.Http.Json;
using NexaOps.Api.IntegrationTests.Infrastructure;
using NexaOps.Application.Requests;
using NexaOps.Domain.Approvals;
using NexaOps.Domain.Catalog;
using NexaOps.Domain.Requests;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Api.IntegrationTests;

/// <summary>
/// The service request module end to end, over real HTTP against real SQL Server.
/// <para>
/// The behaviours worth protecting are that ordering is validated on the server regardless of
/// what the browser sent, that work cannot start before authorisation, and that an approval can
/// only be decided by the person it is addressed to.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RequestLifecycleTests
{
    private readonly TestEnvironment _environment;

    public RequestLifecycleTests(TestEnvironment environment) => _environment = environment;

    // -----------------------------------------------------------------
    // Catalogue
    // -----------------------------------------------------------------

    [Fact]
    public async Task A_catalogue_item_cannot_be_published_without_a_fulfilment_group()
    {
        var admin = await _environment.ClientForAsync(_environment.Acme.Manager);

        var created = await CreateItemAsync(admin, "NO-GROUP", withFulfilmentGroup: false);

        var publish = await admin.PostAsync($"/api/v1/catalog/items/{created.Id}/publish", null);

        // Publishing it would put orders into a queue nobody owns, which the requester
        // experiences as silence.
        publish.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_draft_item_cannot_be_ordered_and_is_invisible_to_a_requester()
    {
        var admin = await _environment.ClientForAsync(_environment.Acme.Manager);
        var employee = await _environment.ClientForAsync(_environment.Acme.Employee);

        var draft = await CreateItemAsync(admin, "DRAFT-ONLY");

        var browse = await employee.GetFromJsonAsync<List<CatalogItemSummaryDto>>(
            "/api/v1/catalog/items", TestEnvironment.Json);

        browse.ShouldNotBeNull();
        browse.ShouldNotContain(i => i.Id == draft.Id);

        // Not found rather than forbidden: the existence of an unpublished item is itself
        // information about what the business is planning.
        var get = await employee.GetAsync($"/api/v1/catalog/items/{draft.Id}");
        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_requester_cannot_edit_the_catalogue()
    {
        var employee = await _environment.ClientForAsync(_environment.Acme.Employee);

        var response = await employee.PostAsJsonAsync("/api/v1/catalog/items", new UpsertCatalogItemCommand
        {
            Code = "SNEAKY",
            Name = "Unauthorised item"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // -----------------------------------------------------------------
    // Ordering and validation
    // -----------------------------------------------------------------

    [Fact]
    public async Task Ordering_validates_every_answer_against_the_items_own_field_definitions()
    {
        var admin = await _environment.ClientForAsync(_environment.Acme.Manager);
        var employee = await _environment.ClientForAsync(_environment.Acme.Employee);

        var item = await PublishItemWithChoiceAsync(admin, "VALIDATED");

        var response = await employee.PostAsJsonAsync("/api/v1/requests", new CreateRequestCommand
        {
            Items =
            [
                new RequestLineInput
                {
                    CatalogItemId = item.Id,
                    Quantity = 1,
                    Values = new Dictionary<string, string> { ["os"] = "Solaris" }
                }
            ]
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem.ShouldNotBeNull();
        problem.Errors.ShouldNotBeNull();

        // The offered-options rule and the required-field rule both fire, each named by the
        // field it belongs to so the form can attach them.
        problem.Errors.Keys.ShouldContain("items[0].values.os");
        problem.Errors.Keys.ShouldContain("items[0].values.cost_centre");
    }

    [Fact]
    public async Task An_answer_to_a_field_the_item_does_not_define_is_refused()
    {
        var admin = await _environment.ClientForAsync(_environment.Acme.Manager);
        var employee = await _environment.ClientForAsync(_environment.Acme.Employee);

        var item = await PublishItemWithChoiceAsync(admin, "NO-SMUGGLING");

        var response = await employee.PostAsJsonAsync("/api/v1/requests", new CreateRequestCommand
        {
            Items =
            [
                new RequestLineInput
                {
                    CatalogItemId = item.Id,
                    Quantity = 1,
                    Values = new Dictionary<string, string>
                    {
                        ["os"] = "Windows 11",
                        ["cost_centre"] = "FIN-01",

                        // A crafted payload must not be able to write arbitrary keys onto a
                        // record just because the browser sent them.
                        ["is_approved"] = "true"
                    }
                }
            ]
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Errors!.Keys.ShouldContain("items[0].values.is_approved");
    }

    [Fact]
    public async Task An_order_line_snapshots_the_items_name_and_price()
    {
        var admin = await _environment.ClientForAsync(_environment.Acme.Manager);
        var employee = await _environment.ClientForAsync(_environment.Acme.Employee);

        var item = await PublishItemWithChoiceAsync(admin, "SNAPSHOT", cost: 74500m);
        var request = await OrderAsync(employee, item.Id);

        var line = request.Items.ShouldHaveSingleItem();
        line.CatalogItemName.ShouldBe(item.Name);
        line.UnitCost.ShouldBe(74500m);
        request.TotalCost.ShouldBe(74500m);

        // Renaming and repricing the catalogue must not rewrite what somebody already ordered,
        // nor what an approver authorised on that basis.
        await admin.PutAsJsonAsync($"/api/v1/catalog/items/{item.Id}", new UpsertCatalogItemCommand
        {
            Code = "SNAPSHOT",
            Name = "Renamed after the order",
            ShortDescription = "Changed",
            FulfilmentGroupId = _environment.Acme.GroupId,
            Cost = 999m,
            Variables = []
        });

        var reloaded = await employee.GetFromJsonAsync<RequestDetailDto>(
            $"/api/v1/requests/{request.Id}", TestEnvironment.Json);

        reloaded!.Items.Single().CatalogItemName.ShouldBe(item.Name);
        reloaded.Items.Single().UnitCost.ShouldBe(74500m);
    }

    [Fact]
    public async Task Quantity_is_capped_at_the_items_configured_maximum()
    {
        var admin = await _environment.ClientForAsync(_environment.Acme.Manager);
        var employee = await _environment.ClientForAsync(_environment.Acme.Employee);

        var item = await PublishItemWithChoiceAsync(admin, "CAPPED", maxQuantity: 2);

        var response = await employee.PostAsJsonAsync("/api/v1/requests", new CreateRequestCommand
        {
            Items =
            [
                new RequestLineInput
                {
                    CatalogItemId = item.Id,
                    Quantity = 9,
                    Values = new Dictionary<string, string>
                    {
                        ["os"] = "Windows 11", ["cost_centre"] = "FIN-01"
                    }
                }
            ]
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // -----------------------------------------------------------------
    // Approval
    // -----------------------------------------------------------------

    [Fact]
    public async Task An_item_needing_approval_routes_the_request_to_the_named_approver()
    {
        var admin = await _environment.ClientForAsync(_environment.Acme.Manager);
        var employee = await _environment.ClientForAsync(_environment.Acme.Employee);

        var item = await PublishItemWithChoiceAsync(
            admin, "NEEDS-APPROVAL", approverUserId: _environment.Acme.Manager.Id);

        var request = await OrderAsync(employee, item.Id);

        request.Status.ShouldBe(RequestStatus.AwaitingApproval);

        var approval = request.Approvals.ShouldHaveSingleItem();
        approval.State.ShouldBe(ApprovalState.Pending);
        approval.ApproverUserId.ShouldBe(_environment.Acme.Manager.Id);
    }

    [Fact]
    public async Task An_approval_can_only_be_decided_by_the_person_it_is_addressed_to()
    {
        var admin = await _environment.ClientForAsync(_environment.Acme.Manager);
        var employee = await _environment.ClientForAsync(_environment.Acme.Employee);
        var agent = await _environment.ClientForAsync(_environment.Acme.Agent);

        var item = await PublishItemWithChoiceAsync(
            admin, "ADDRESSEE-ONLY", approverUserId: _environment.Acme.Manager.Id);

        var request = await OrderAsync(employee, item.Id);
        var approvalId = request.Approvals.Single().Id;

        // The requester approving their own request is the case this exists to stop.
        var byRequester = await employee.PostAsJsonAsync(
            $"/api/v1/approvals/{approvalId}/decide", new DecideApprovalCommand { Approved = true });

        // 404, not 403: confirming the approval exists discloses work they are not part of.
        byRequester.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var byBystander = await agent.PostAsJsonAsync(
            $"/api/v1/approvals/{approvalId}/decide", new DecideApprovalCommand { Approved = true });

        byBystander.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And the request is still waiting, unchanged.
        var reloaded = await employee.GetFromJsonAsync<RequestDetailDto>(
            $"/api/v1/requests/{request.Id}", TestEnvironment.Json);

        reloaded!.Status.ShouldBe(RequestStatus.AwaitingApproval);
    }

    [Fact]
    public async Task Rejecting_requires_a_reason_and_ends_the_request()
    {
        var admin = await _environment.ClientForAsync(_environment.Acme.Manager);
        var employee = await _environment.ClientForAsync(_environment.Acme.Employee);
        var manager = await _environment.ClientForAsync(_environment.Acme.Manager);

        var item = await PublishItemWithChoiceAsync(
            admin, "REJECTED", approverUserId: _environment.Acme.Manager.Id);

        var request = await OrderAsync(employee, item.Id);
        var approvalId = request.Approvals.Single().Id;

        var noReason = await manager.PostAsJsonAsync(
            $"/api/v1/approvals/{approvalId}/decide", new DecideApprovalCommand { Approved = false });

        noReason.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var withReason = await manager.PostAsJsonAsync(
            $"/api/v1/approvals/{approvalId}/decide",
            new DecideApprovalCommand { Approved = false, Comment = "Not in this year's budget." });

        withReason.EnsureSuccessStatusCode();

        var decided = await withReason.Content.ReadFromJsonAsync<RequestDetailDto>(TestEnvironment.Json);
        decided!.Status.ShouldBe(RequestStatus.Rejected);
        decided.RejectionReason.ShouldBe("Not in this year's budget.");

        // Terminal. A new request is raised rather than re-submitting this one, which keeps
        // approval cycle-time reporting honest.
        decided.AllowedTransitions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_settled_approval_cannot_be_decided_again()
    {
        var admin = await _environment.ClientForAsync(_environment.Acme.Manager);
        var employee = await _environment.ClientForAsync(_environment.Acme.Employee);
        var manager = await _environment.ClientForAsync(_environment.Acme.Manager);

        var item = await PublishItemWithChoiceAsync(
            admin, "ONCE-ONLY", approverUserId: _environment.Acme.Manager.Id);

        var request = await OrderAsync(employee, item.Id);
        var approvalId = request.Approvals.Single().Id;

        var first = await manager.PostAsJsonAsync(
            $"/api/v1/approvals/{approvalId}/decide",
            new DecideApprovalCommand { Approved = true, Comment = "Fine." });

        first.EnsureSuccessStatusCode();

        var second = await manager.PostAsJsonAsync(
            $"/api/v1/approvals/{approvalId}/decide",
            new DecideApprovalCommand { Approved = false, Comment = "Changed my mind." });

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // -----------------------------------------------------------------
    // Fulfilment
    // -----------------------------------------------------------------

    [Fact]
    public async Task Work_cannot_start_before_the_request_is_authorised()
    {
        var admin = await _environment.ClientForAsync(_environment.Acme.Manager);
        var employee = await _environment.ClientForAsync(_environment.Acme.Employee);
        var agent = await _environment.ClientForAsync(_environment.Acme.Agent);

        var item = await PublishItemWithChoiceAsync(
            admin, "UNAUTHORISED", approverUserId: _environment.Acme.Manager.Id);

        var request = await OrderAsync(employee, item.Id);
        var lineId = request.Items.Single().Id;

        var fulfil = await agent.PostAsJsonAsync(
            $"/api/v1/requests/{request.Id}/items/{lineId}/fulfil",
            new FulfilRequestItemCommand { Notes = "Delivered early." });

        fulfil.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_request_runs_from_order_to_closure()
    {
        var admin = await _environment.ClientForAsync(_environment.Acme.Manager);
        var employee = await _environment.ClientForAsync(_environment.Acme.Employee);
        var agent = await _environment.ClientForAsync(_environment.Acme.Agent);

        var item = await PublishItemWithChoiceAsync(admin, "FULL-LIFECYCLE");
        var request = await OrderAsync(employee, item.Id);

        // No approval configured, so it is authorised immediately.
        request.Status.ShouldBe(RequestStatus.Approved);

        var lineId = request.Items.Single().Id;

        var fulfilled = await agent.PostAsJsonAsync(
            $"/api/v1/requests/{request.Id}/items/{lineId}/fulfil",
            new FulfilRequestItemCommand { Notes = "Asset NX-1042." });

        fulfilled.EnsureSuccessStatusCode();

        var afterLine = await fulfilled.Content.ReadFromJsonAsync<RequestDetailDto>(TestEnvironment.Json);

        // Delivering the first line moves the request into active fulfilment on its own.
        afterLine!.Status.ShouldBe(RequestStatus.InProgress);
        afterLine.Items.Single().Status.ShouldBe(RequestItemStatus.Fulfilled);
        afterLine.Items.Single().FulfilmentNotes.ShouldBe("Asset NX-1042.");

        var complete = await agent.PostAsJsonAsync(
            $"/api/v1/requests/{request.Id}/status",
            new ChangeRequestStatusCommand { Status = RequestStatus.Fulfilled });

        complete.EnsureSuccessStatusCode();

        var closed = await agent.PostAsJsonAsync(
            $"/api/v1/requests/{request.Id}/status",
            new ChangeRequestStatusCommand { Status = RequestStatus.Closed });

        closed.EnsureSuccessStatusCode();

        var final = await closed.Content.ReadFromJsonAsync<RequestDetailDto>(TestEnvironment.Json);
        final!.Status.ShouldBe(RequestStatus.Closed);
        final.AllowedTransitions.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_request_cannot_be_completed_while_a_line_is_outstanding()
    {
        var admin = await _environment.ClientForAsync(_environment.Acme.Manager);
        var employee = await _environment.ClientForAsync(_environment.Acme.Employee);
        var agent = await _environment.ClientForAsync(_environment.Acme.Agent);

        var item = await PublishItemWithChoiceAsync(admin, "TWO-LINES");
        var request = await OrderAsync(employee, item.Id, quantity: 1, lines: 2);

        request.Items.Count.ShouldBe(2);

        await agent.PostAsJsonAsync(
            $"/api/v1/requests/{request.Id}/items/{request.Items.First().Id}/fulfil",
            new FulfilRequestItemCommand());

        var complete = await agent.PostAsJsonAsync(
            $"/api/v1/requests/{request.Id}/status",
            new ChangeRequestStatusCommand { Status = RequestStatus.Fulfilled });

        complete.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_illegal_transition_is_refused_with_the_move_that_was_attempted()
    {
        var admin = await _environment.ClientForAsync(_environment.Acme.Manager);
        var employee = await _environment.ClientForAsync(_environment.Acme.Employee);
        var agent = await _environment.ClientForAsync(_environment.Acme.Agent);

        var item = await PublishItemWithChoiceAsync(admin, "ILLEGAL-MOVE");
        var request = await OrderAsync(employee, item.Id);

        var response = await agent.PostAsJsonAsync(
            $"/api/v1/requests/{request.Id}/status",
            new ChangeRequestStatusCommand { Status = RequestStatus.Closed });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("request.invalid_transition");
    }

    // -----------------------------------------------------------------
    // SLA
    // -----------------------------------------------------------------

    [Fact]
    public async Task A_request_awaiting_approval_has_a_clock_that_is_not_running()
    {
        var admin = await _environment.ClientForAsync(_environment.Acme.Manager);
        var employee = await _environment.ClientForAsync(_environment.Acme.Employee);

        var item = await PublishItemWithChoiceAsync(
            admin, "SLA-PAUSED", approverUserId: _environment.Acme.Manager.Id);

        var request = await OrderAsync(employee, item.Id);

        request.Status.ShouldBe(RequestStatus.AwaitingApproval);

        // A commitment exists and its deadline is known, but the service desk is not being
        // charged for time it has not been authorised to act in.
        request.NextSlaDueAt.ShouldNotBeNull();
        request.HasBreachedSla.ShouldBeFalse();
    }

    [Fact]
    public async Task Approval_starts_the_fulfilment_clock_and_delivery_meets_it()
    {
        var admin = await _environment.ClientForAsync(_environment.Acme.Manager);
        var employee = await _environment.ClientForAsync(_environment.Acme.Employee);
        var manager = await _environment.ClientForAsync(_environment.Acme.Manager);

        var item = await PublishItemWithChoiceAsync(
            admin, "SLA-RUNS", approverUserId: _environment.Acme.Manager.Id);

        var request = await OrderAsync(employee, item.Id);

        var approved = await manager.PostAsJsonAsync(
            $"/api/v1/approvals/{request.Approvals.Single().Id}/decide",
            new DecideApprovalCommand { Approved = true });

        approved.EnsureSuccessStatusCode();

        var running = await approved.Content.ReadFromJsonAsync<RequestDetailDto>(TestEnvironment.Json);
        running!.Status.ShouldBe(RequestStatus.Approved);
        running.NextSlaDueAt.ShouldNotBeNull();

        await manager.PostAsJsonAsync(
            $"/api/v1/requests/{request.Id}/items/{request.Items.Single().Id}/fulfil",
            new FulfilRequestItemCommand());

        var completed = await manager.PostAsJsonAsync(
            $"/api/v1/requests/{request.Id}/status",
            new ChangeRequestStatusCommand { Status = RequestStatus.Fulfilled });

        completed.EnsureSuccessStatusCode();

        var final = await completed.Content.ReadFromJsonAsync<RequestDetailDto>(TestEnvironment.Json);

        // The commitment was met, so there is no live clock left and nothing breached.
        final!.HasBreachedSla.ShouldBeFalse();
        final.NextSlaDueAt.ShouldBeNull();
    }

    [Fact]
    public async Task Cancelling_abandons_the_clock_rather_than_breaching_it()
    {
        var admin = await _environment.ClientForAsync(_environment.Acme.Manager);
        var employee = await _environment.ClientForAsync(_environment.Acme.Employee);

        var item = await PublishItemWithChoiceAsync(admin, "SLA-CANCEL");
        var request = await OrderAsync(employee, item.Id);

        var cancelled = await employee.PostAsJsonAsync($"/api/v1/requests/{request.Id}/cancel",
            new CancelRequestCommand { Reason = "No longer needed." });

        cancelled.EnsureSuccessStatusCode();

        var final = await cancelled.Content.ReadFromJsonAsync<RequestDetailDto>(TestEnvironment.Json);

        // Nobody failed a commitment on work that was called off.
        final!.HasBreachedSla.ShouldBeFalse();
        final.NextSlaDueAt.ShouldBeNull();
    }

    // -----------------------------------------------------------------
    // Visibility
    // -----------------------------------------------------------------

    [Fact]
    public async Task A_requester_never_sees_internal_work_notes()
    {
        var admin = await _environment.ClientForAsync(_environment.Acme.Manager);
        var employee = await _environment.ClientForAsync(_environment.Acme.Employee);
        var agent = await _environment.ClientForAsync(_environment.Acme.Agent);

        var item = await PublishItemWithChoiceAsync(admin, "WORK-NOTES");
        var request = await OrderAsync(employee, item.Id);

        await agent.PostAsJsonAsync($"/api/v1/requests/{request.Id}/comments",
            new AddRequestCommentCommand
            {
                Body = "Internal: licence pool nearly exhausted.",
                Kind = IncidentCommentKind.WorkNote
            });

        await agent.PostAsJsonAsync($"/api/v1/requests/{request.Id}/comments",
            new AddRequestCommentCommand
            {
                Body = "Your licence has been issued.",
                Kind = IncidentCommentKind.PublicComment
            });

        var agentView = await agent.GetFromJsonAsync<List<RequestCommentDto>>(
            $"/api/v1/requests/{request.Id}/comments", TestEnvironment.Json);

        var requesterView = await employee.GetFromJsonAsync<List<RequestCommentDto>>(
            $"/api/v1/requests/{request.Id}/comments", TestEnvironment.Json);

        agentView!.Count.ShouldBe(2);

        // Filtered at the query level, not hidden in the browser.
        requesterView!.ShouldHaveSingleItem().Kind.ShouldBe(IncidentCommentKind.PublicComment);
        requesterView.ShouldNotContain(c => c.Body.Contains("licence pool"));
    }

    [Fact]
    public async Task A_requester_may_withdraw_their_own_request_without_the_cancel_permission()
    {
        var admin = await _environment.ClientForAsync(_environment.Acme.Manager);
        var employee = await _environment.ClientForAsync(_environment.Acme.Employee);

        var item = await PublishItemWithChoiceAsync(admin, "WITHDRAW");
        var request = await OrderAsync(employee, item.Id);

        var response = await employee.PostAsJsonAsync($"/api/v1/requests/{request.Id}/cancel",
            new CancelRequestCommand { Reason = "No longer needed." });

        response.EnsureSuccessStatusCode();

        var cancelled = await response.Content.ReadFromJsonAsync<RequestDetailDto>(TestEnvironment.Json);
        cancelled!.Status.ShouldBe(RequestStatus.Cancelled);
        cancelled.CancellationReason.ShouldBe("No longer needed.");

        // Undelivered lines are cancelled with it.
        cancelled.Items.ShouldAllBe(i => i.Status == RequestItemStatus.Cancelled);
    }

    [Fact]
    public async Task An_unrecognised_sort_field_is_a_validation_failure_not_a_conflict()
    {
        var agent = await _environment.ClientForAsync(_environment.Acme.Agent);

        var response = await agent.GetAsync("/api/v1/requests?sortBy=; DROP TABLE ServiceRequests");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <param name="withFulfilmentGroup">
    /// False creates an item with no fulfilment group. A nullable Guid parameter cannot express
    /// this: an explicit null is indistinguishable from "not supplied" once the default is null.
    /// </param>
    private async Task<CatalogItemDetailDto> CreateItemAsync(
        HttpClient client,
        string code,
        bool withFulfilmentGroup = true)
    {
        var response = await client.PostAsJsonAsync("/api/v1/catalog/items", new UpsertCatalogItemCommand
        {
            Code = $"{code}-{Guid.NewGuid():N}"[..20],
            Name = $"Item {code}",
            ShortDescription = "Created by the integration suite.",
            FulfilmentGroupId = withFulfilmentGroup ? _environment.Acme.GroupId : null,
            Variables = []
        });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CatalogItemDetailDto>(TestEnvironment.Json))!;
    }

    /// <summary>A published item with one required choice field and one required text field.</summary>
    private async Task<CatalogItemDetailDto> PublishItemWithChoiceAsync(
        HttpClient client,
        string code,
        Guid? approverUserId = null,
        decimal? cost = null,
        int? maxQuantity = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/catalog/items", new UpsertCatalogItemCommand
        {
            Code = $"{code}-{Guid.NewGuid():N}"[..20],
            Name = $"Item {code}",
            ShortDescription = "Created by the integration suite.",
            FulfilmentGroupId = _environment.Acme.GroupId,
            Cost = cost,
            MaxQuantity = maxQuantity,
            RequiresApproval = approverUserId is not null,
            ApprovalTargetKind = ApprovalTargetKind.User,
            ApproverUserId = approverUserId,
            ApprovalRule = ApprovalRule.AnyOne,
            Variables =
            [
                new UpsertCatalogVariableCommand
                {
                    Key = "os", Label = "Operating system", Type = VariableType.Choice,
                    IsRequired = true, SortOrder = 1, Choices = ["Windows 11", "Ubuntu 24.04"]
                },
                new UpsertCatalogVariableCommand
                {
                    Key = "cost_centre", Label = "Cost centre", Type = VariableType.Text,
                    IsRequired = true, SortOrder = 2, MaxLength = 10
                }
            ]
        });

        response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CatalogItemDetailDto>(TestEnvironment.Json))!;

        var publish = await client.PostAsync($"/api/v1/catalog/items/{created.Id}/publish", null);
        publish.EnsureSuccessStatusCode();

        return (await publish.Content.ReadFromJsonAsync<CatalogItemDetailDto>(TestEnvironment.Json))!;
    }

    private static async Task<RequestDetailDto> OrderAsync(
        HttpClient client,
        Guid catalogItemId,
        int quantity = 1,
        int lines = 1)
    {
        var command = new CreateRequestCommand { Items = [] };

        for (var i = 0; i < lines; i++)
        {
            command.Items.Add(new RequestLineInput
            {
                CatalogItemId = catalogItemId,
                Quantity = quantity,
                Values = new Dictionary<string, string>
                {
                    ["os"] = "Windows 11",
                    ["cost_centre"] = "FIN-01"
                }
            });
        }

        var response = await client.PostAsJsonAsync("/api/v1/requests", command);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<RequestDetailDto>(TestEnvironment.Json))!;
    }
}

/// <summary>
/// Minimal shape of an RFC 9457 response, enough to assert on the stable machine-readable code
/// and the per-field validation messages.
/// </summary>
public sealed record ProblemDetailsResponse
{
    public string? Code { get; init; }
    public string? Title { get; init; }
    public string? Detail { get; init; }
    public Dictionary<string, string[]>? Errors { get; init; }
}
