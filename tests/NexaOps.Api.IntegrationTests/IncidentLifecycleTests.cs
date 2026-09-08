using System.Net;
using System.Net.Http.Json;
using NexaOps.Api.IntegrationTests.Infrastructure;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;
using NexaOps.Application.Incidents;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Sla;

namespace NexaOps.Api.IntegrationTests;

/// <summary>
/// The incident module end to end: raise, triage, assign, converse, resolve, close.
/// <para>
/// These run over HTTP against the real database, so they cover the things unit tests cannot -
/// number allocation, SLA attachment, concurrency tokens, and the audit trail actually being
/// written.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class IncidentLifecycleTests
{
    private readonly TestEnvironment _env;

    public IncidentLifecycleTests(TestEnvironment env) => _env = env;

    private async Task<IncidentDetailDto> RaiseAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/incidents", body, TestEnvironment.Json);

        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Expected 201 but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<IncidentDetailDto>(TestEnvironment.Json))!;
    }

    private static object NewIncident(
        string title,
        string impact = "Moderate",
        string urgency = "Medium",
        Guid? categoryId = null) => new
        {
            title,
            description = "Raised by the incident lifecycle suite to exercise the module end to end.",
            impact,
            urgency,
            categoryId
        };

    // -----------------------------------------------------------------
    // Creation
    // -----------------------------------------------------------------

    [Fact]
    public async Task Raising_an_incident_allocates_a_number_and_derives_a_priority()
    {
        var client = await _env.ClientForAsync(_env.Acme.Agent);

        var incident = await RaiseAsync(client, NewIncident("Wifi keeps dropping on the third floor"));

        incident.Number.ShouldStartWith("INC");
        incident.Number.Length.ShouldBe(10);
        incident.Status.ShouldBe(IncidentStatus.New);

        // Moderate impact with medium urgency is a P3 on the default matrix.
        incident.Priority.ShouldBe(Priority.P3Moderate);
        incident.IsPriorityOverridden.ShouldBeFalse();
    }

    [Fact]
    public async Task Incident_numbers_are_sequential_and_unique()
    {
        var client = await _env.ClientForAsync(_env.Acme.Agent);

        var numbers = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            numbers.Add((await RaiseAsync(client, NewIncident($"Sequential allocation probe {i}"))).Number);
        }

        numbers.Distinct(StringComparer.Ordinal).Count().ShouldBe(numbers.Count);

        // Zero-padded, so the string order matches the numeric order.
        numbers.ShouldBe(numbers.OrderBy(n => n, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public async Task Raising_an_incident_attaches_the_sla_commitments_for_its_priority()
    {
        var client = await _env.ClientForAsync(_env.Acme.Agent);

        var incident = await RaiseAsync(client, NewIncident(
            "Production database is unreachable", impact: "Extensive", urgency: "Critical"));

        incident.Priority.ShouldBe(Priority.P1Critical);
        incident.SlaInstances.Count.ShouldBe(2);

        var response = incident.SlaInstances.Single(s => s.TargetType == SlaTargetType.Response);
        var resolution = incident.SlaInstances.Single(s => s.TargetType == SlaTargetType.Resolution);

        response.State.ShouldBe(SlaState.InProgress);
        response.DurationMinutes.ShouldBe(15);

        // A P1 runs on the 24x7 calendar, so four hours of wall clock from creation.
        resolution.DurationMinutes.ShouldBe(240);
        resolution.DueAt.ShouldBe(incident.CreatedAt.AddMinutes(240), TimeSpan.FromSeconds(5));

        // The response commitment is the tighter of the two, so it is the next deadline to watch.
        response.DueAt.ShouldBeLessThan(resolution.DueAt);
    }

    [Fact]
    public async Task A_category_routes_the_incident_to_its_default_group()
    {
        var client = await _env.ClientForAsync(_env.Acme.Agent);

        var incident = await RaiseAsync(client, NewIncident(
            "Cannot connect to the VPN", categoryId: _env.Acme.CategoryId));

        incident.AssignmentGroupId.ShouldBe(_env.Acme.GroupId);
        incident.CategoryName.ShouldBe("Network and connectivity");
    }

    [Fact]
    public async Task An_incident_without_a_title_is_rejected_with_field_level_errors()
    {
        var client = await _env.ClientForAsync(_env.Acme.Agent);

        var response = await client.PostAsJsonAsync(
            "/api/v1/incidents",
            new { title = "", description = "", impact = "Moderate", urgency = "Medium" },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("validation_failed");
        body.ShouldContain("title");
        body.ShouldContain("description");
    }

    // -----------------------------------------------------------------
    // Assignment
    // -----------------------------------------------------------------

    [Fact]
    public async Task Assigning_to_an_agent_advances_a_new_incident_to_assigned()
    {
        var client = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await RaiseAsync(client, NewIncident("Incident awaiting an owner"));

        var response = await client.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/assign",
            new { assignmentGroupId = _env.Acme.GroupId, assignedToUserId = _env.Acme.Agent.Id },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var assigned = await response.Content.ReadFromJsonAsync<IncidentDetailDto>(TestEnvironment.Json);
        assigned!.Status.ShouldBe(IncidentStatus.Assigned);
        assigned.AssignedToUserId.ShouldBe(_env.Acme.Agent.Id);
        assigned.AssignedToName.ShouldBe("Kavya Nair");
    }

    [Fact]
    public async Task An_assignee_must_belong_to_the_assignment_group()
    {
        var client = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await RaiseAsync(client, NewIncident("Incident for a mismatched assignment"));

        // The agent is not a member of the Network Operations group.
        var response = await client.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/assign",
            new { assignmentGroupId = _env.Acme.OtherGroupId, assignedToUserId = _env.Acme.Agent.Id },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldContain("incident.assignee_not_in_group");
    }

    // -----------------------------------------------------------------
    // Conversation and first response
    // -----------------------------------------------------------------

    [Fact]
    public async Task An_agents_first_public_reply_stops_the_response_clock()
    {
        var employee = await _env.ClientForAsync(_env.Acme.Employee);
        var incident = await RaiseAsync(employee, NewIncident("My laptop will not start"));

        incident.FirstRespondedAt.ShouldBeNull();

        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        var reply = await agent.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/comments",
            new { body = "Thanks for reporting this - I have arranged a loan laptop.", kind = "PublicComment" },
            TestEnvironment.Json);

        reply.StatusCode.ShouldBe(HttpStatusCode.Created);

        var updated = await agent.GetFromJsonAsync<IncidentDetailDto>(
            $"/api/v1/incidents/{incident.Id}", TestEnvironment.Json);

        updated!.FirstRespondedAt.ShouldNotBeNull();

        updated.SlaInstances.Single(s => s.TargetType == SlaTargetType.Response)
            .State.ShouldBe(SlaState.Met);
    }

    [Fact]
    public async Task A_requesters_own_comment_does_not_count_as_an_agent_response()
    {
        var employee = await _env.ClientForAsync(_env.Acme.Employee);
        var incident = await RaiseAsync(employee, NewIncident("Adding detail to my own ticket"));

        await employee.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/comments",
            new { body = "Some extra detail I forgot to mention.", kind = "PublicComment" },
            TestEnvironment.Json);

        var updated = await employee.GetFromJsonAsync<IncidentDetailDto>(
            $"/api/v1/incidents/{incident.Id}", TestEnvironment.Json);

        // Otherwise a chatty requester would silently satisfy the desk's response commitment.
        updated!.FirstRespondedAt.ShouldBeNull();
    }

    // -----------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------

    [Fact]
    public async Task Resolving_requires_a_resolution_code_and_notes()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);
        var incident = await RaiseAsync(agent, NewIncident("Incident resolved without detail"));

        var response = await agent.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/status",
            new { status = "Resolved" },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_incident_can_be_taken_through_its_whole_lifecycle()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var incident = await RaiseAsync(agent, NewIncident("Shared drive is inaccessible"));

        // Assign
        await manager.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/assign",
            new { assignmentGroupId = _env.Acme.GroupId, assignedToUserId = _env.Acme.Agent.Id },
            TestEnvironment.Json);

        // Start work
        var started = await agent.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/status",
            new { status = "InProgress" },
            TestEnvironment.Json);
        started.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Wait on the requester
        var pending = await agent.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/status",
            new { status = "Pending", pendingReason = "AwaitingRequester", notes = "Asked for their machine name." },
            TestEnvironment.Json);
        pending.StatusCode.ShouldBe(HttpStatusCode.OK);

        var paused = await pending.Content.ReadFromJsonAsync<IncidentDetailDto>(TestEnvironment.Json);
        paused!.SlaInstances.Single(s => s.TargetType == SlaTargetType.Resolution)
            .State.ShouldBe(SlaState.Paused);

        // Resume
        await agent.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/status",
            new { status = "InProgress" },
            TestEnvironment.Json);

        // Resolve
        var resolved = await agent.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/status",
            new
            {
                status = "Resolved",
                resolutionCode = "Resolved",
                notes = "Restored the user's membership of the shared drive security group."
            },
            TestEnvironment.Json);

        resolved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterResolve = await resolved.Content.ReadFromJsonAsync<IncidentDetailDto>(TestEnvironment.Json);

        afterResolve!.Status.ShouldBe(IncidentStatus.Resolved);
        afterResolve.ResolvedAt.ShouldNotBeNull();
        afterResolve.ResolvedByName.ShouldBe("Kavya Nair");

        afterResolve.SlaInstances.Single(s => s.TargetType == SlaTargetType.Resolution)
            .State.ShouldBe(SlaState.Met);

        // Close
        var closed = await agent.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/status",
            new { status = "Closed" },
            TestEnvironment.Json);

        closed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterClose = await closed.Content.ReadFromJsonAsync<IncidentDetailDto>(TestEnvironment.Json);

        afterClose!.Status.ShouldBe(IncidentStatus.Closed);
        afterClose.ClosedAt.ShouldNotBeNull();
        afterClose.AllowedTransitions.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_illegal_transition_is_refused_with_a_machine_readable_code()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);
        var incident = await RaiseAsync(agent, NewIncident("Incident closed straight from new"));

        var response = await agent.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/status",
            new { status = "Closed" },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldContain("incident.invalid_transition");
    }

    [Fact]
    public async Task A_closed_incident_cannot_receive_new_comments()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);
        var incident = await RaiseAsync(agent, NewIncident("Incident that will be closed"));

        await agent.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/status",
            new { status = "Resolved", resolutionCode = "Resolved", notes = "Fixed by restarting the service." },
            TestEnvironment.Json);

        await agent.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/status",
            new { status = "Closed" },
            TestEnvironment.Json);

        var response = await agent.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/comments",
            new { body = "One more thought.", kind = "PublicComment" },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldContain("incident.closed_no_comments");
    }

    [Fact]
    public async Task Reopening_a_resolved_incident_requires_a_reason_and_counts_the_reopen()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await RaiseAsync(manager, NewIncident("Incident that will be reopened"));

        await manager.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/status",
            new { status = "Resolved", resolutionCode = "Resolved", notes = "Believed fixed by a config change." },
            TestEnvironment.Json);

        var withoutReason = await manager.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/status",
            new { status = "InProgress" },
            TestEnvironment.Json);

        withoutReason.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var reopened = await manager.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/status",
            new { status = "InProgress", notes = "The requester reports the problem has returned." },
            TestEnvironment.Json);

        reopened.StatusCode.ShouldBe(HttpStatusCode.OK);

        var detail = await reopened.Content.ReadFromJsonAsync<IncidentDetailDto>(TestEnvironment.Json);
        detail!.Status.ShouldBe(IncidentStatus.InProgress);
        detail.ReopenCount.ShouldBe(1);
        detail.ResolutionCode.ShouldBeNull();
    }

    // -----------------------------------------------------------------
    // Priority
    // -----------------------------------------------------------------

    [Fact]
    public async Task Escalating_priority_re_targets_the_sla_and_records_the_override()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await RaiseAsync(manager, NewIncident("Incident that will be escalated"));

        incident.Priority.ShouldBe(Priority.P3Moderate);
        var originalDue = incident.SlaInstances.Single(s => s.TargetType == SlaTargetType.Resolution).DueAt;

        var response = await manager.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/priority",
            new
            {
                overridePriority = "P1Critical",
                overrideReason = "This is blocking the quarter-end close for the whole finance team."
            },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var escalated = await response.Content.ReadFromJsonAsync<IncidentDetailDto>(TestEnvironment.Json);
        escalated!.Priority.ShouldBe(Priority.P1Critical);
        escalated.IsPriorityOverridden.ShouldBeTrue();
        escalated.PriorityOverrideReason.ShouldContain("quarter-end close");

        // The commitment tightens to the P1 target, measured from the original creation time.
        var newDue = escalated.SlaInstances.Single(s => s.TargetType == SlaTargetType.Resolution).DueAt;
        newDue.ShouldBeLessThan(originalDue);
    }

    [Fact]
    public async Task An_override_without_a_reason_is_refused()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await RaiseAsync(manager, NewIncident("Incident for an unreasoned override"));

        var response = await manager.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/priority",
            new { overridePriority = "P1Critical" },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // -----------------------------------------------------------------
    // Concurrency and audit
    // -----------------------------------------------------------------

    [Fact]
    public async Task A_stale_concurrency_token_is_refused_rather_than_overwriting_a_colleague()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await RaiseAsync(manager, NewIncident("Incident edited by two people"));

        var staleToken = incident.ConcurrencyToken;
        staleToken.ShouldNotBeNull();

        // A colleague saves first.
        var first = await manager.PatchAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}",
            new { title = "Edited first" },
            TestEnvironment.Json);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Our edit still carries the token we read before their change.
        var second = await manager.PatchAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}",
            new { title = "Edited second", concurrencyToken = staleToken },
            TestEnvironment.Json);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.Content.ReadAsStringAsync()).ShouldContain("concurrency.conflict");
    }

    [Fact]
    public async Task Changes_to_an_incident_appear_in_its_audit_history()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await RaiseAsync(manager, NewIncident("Incident whose history is inspected"));

        await manager.PatchAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}",
            new { title = "Renamed for the audit trail" },
            TestEnvironment.Json);

        var audit = await manager.GetFromJsonAsync<List<AuditEventDto>>(
            $"/api/v1/audit/Incident/{incident.Id}", TestEnvironment.Json);

        audit.ShouldNotBeNull();
        audit.ShouldContain(a => a.Action == "Create");

        var update = audit.FirstOrDefault(a => a.ChangedFields is not null && a.ChangedFields.Contains("Title"));
        update.ShouldNotBeNull();
        update.BeforeJson.ShouldContain("Incident whose history is inspected");
        update.AfterJson.ShouldContain("Renamed for the audit trail");
        update.ActorDisplayName.ShouldBe("Priya Raghavan");
    }

    [Fact]
    public async Task The_activity_timeline_merges_comments_and_field_changes()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var incident = await RaiseAsync(manager, NewIncident("Incident with a mixed timeline"));

        await manager.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/comments",
            new { body = "Investigating now.", kind = "PublicComment" },
            TestEnvironment.Json);

        await manager.PatchAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}",
            new { title = "Renamed while investigating" },
            TestEnvironment.Json);

        var activity = await manager.GetFromJsonAsync<List<IncidentActivityDto>>(
            $"/api/v1/incidents/{incident.Id}/activity", TestEnvironment.Json);

        activity.ShouldNotBeNull();
        activity.ShouldContain(a => a.Type == "comment");
        activity.ShouldContain(a => a.Type == "field_change");

        // Newest first, so the timeline reads the way an agent expects.
        activity.Select(a => a.OccurredAt)
            .ShouldBe(activity.Select(a => a.OccurredAt).OrderByDescending(o => o).ToList());
    }

    // -----------------------------------------------------------------
    // Search
    // -----------------------------------------------------------------

    [Fact]
    public async Task Search_filters_by_status_priority_and_free_text()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        var marker = $"searchmarker{Guid.NewGuid():N}"[..24];
        await RaiseAsync(agent, NewIncident($"Critical outage {marker}", "Extensive", "Critical"));
        await RaiseAsync(agent, NewIncident($"Minor annoyance {marker}", "Minor", "Low"));

        var all = await agent.GetFromJsonAsync<PagedResult<IncidentListItemDto>>(
            $"/api/v1/incidents?search={marker}&pageSize=50", TestEnvironment.Json);
        all!.TotalCount.ShouldBe(2);

        var critical = await agent.GetFromJsonAsync<PagedResult<IncidentListItemDto>>(
            $"/api/v1/incidents?search={marker}&priority=P1Critical&pageSize=50", TestEnvironment.Json);
        critical!.TotalCount.ShouldBe(1);
        critical.Items[0].Title.ShouldContain("Critical outage");

        var open = await agent.GetFromJsonAsync<PagedResult<IncidentListItemDto>>(
            $"/api/v1/incidents?search={marker}&openOnly=true&pageSize=50", TestEnvironment.Json);
        open!.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task An_unsupported_sort_field_is_refused_rather_than_interpolated()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        var response = await agent.GetAsync("/api/v1/incidents?sortBy=title;DROP%20TABLE%20Incidents");

        // Sorting is allow-listed, so an unrecognised field never reaches the query builder.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Paging_reports_totals_independently_of_the_page_size()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        var marker = $"pagemarker{Guid.NewGuid():N}"[..22];
        for (var i = 0; i < 5; i++)
        {
            await RaiseAsync(agent, NewIncident($"Paging probe {i} {marker}"));
        }

        var firstPage = await agent.GetFromJsonAsync<PagedResult<IncidentListItemDto>>(
            $"/api/v1/incidents?search={marker}&page=1&pageSize=2", TestEnvironment.Json);

        firstPage!.TotalCount.ShouldBe(5);
        firstPage.Items.Count.ShouldBe(2);
        firstPage.TotalPages.ShouldBe(3);
        firstPage.HasNext.ShouldBeTrue();
        firstPage.HasPrevious.ShouldBeFalse();

        var lastPage = await agent.GetFromJsonAsync<PagedResult<IncidentListItemDto>>(
            $"/api/v1/incidents?search={marker}&page=3&pageSize=2", TestEnvironment.Json);

        lastPage!.Items.Count.ShouldBe(1);
        lastPage.HasNext.ShouldBeFalse();
    }

    [Fact]
    public async Task Page_size_is_capped_regardless_of_what_the_client_asks_for()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        var page = await agent.GetFromJsonAsync<PagedResult<IncidentListItemDto>>(
            "/api/v1/incidents?pageSize=100000", TestEnvironment.Json);

        page!.PageSize.ShouldBe(PagedQuery.MaxPageSize);
    }
}
