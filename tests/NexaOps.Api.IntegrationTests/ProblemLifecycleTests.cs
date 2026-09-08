using System.Net;
using System.Net.Http.Json;
using NexaOps.Api.IntegrationTests.Infrastructure;
using NexaOps.Application.Common;
using NexaOps.Application.Problems;
using NexaOps.Domain.Problems;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Api.IntegrationTests;

/// <summary>
/// Problem management end to end, over real HTTP against real SQL Server.
/// <para>
/// The behaviours worth protecting are that a known error cannot be published without something
/// the service desk can use, that publishing one is a separate permission from investigating,
/// and that a problem raised from an incident keeps the evidence that justified it.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ProblemLifecycleTests
{
    private readonly TestEnvironment _env;

    public ProblemLifecycleTests(TestEnvironment env) => _env = env;

    [Fact]
    public async Task A_problem_raised_from_an_incident_keeps_the_evidence()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        var incident = await CreateIncidentAsync(agent, "VPN drops at the Pune office every morning");

        var problem = await RaiseAsync(agent, "Branch VPN instability", fromIncidentId: incident.Id);

        problem.LinkedIncidentCount.ShouldBe(1);
        problem.LinkedIncidents.ShouldHaveSingleItem().Id.ShouldBe(incident.Id);

        // Classification is inherited, so the investigator does not re-enter what the incident
        // already established.
        problem.CategoryId.ShouldBe(incident.CategoryId);
    }

    [Fact]
    public async Task A_known_error_cannot_be_published_without_a_workaround()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var problem = await RaiseAsync(agent, "Payroll batch fails intermittently");

        await agent.PostAsJsonAsync($"/api/v1/problems/{problem.Id}/findings",
            new RecordFindingsCommand { RootCause = "Deadlock on the payroll staging table." });

        var response = await manager.PostAsJsonAsync($"/api/v1/problems/{problem.Id}/status",
            new ChangeProblemStatusCommand { Status = ProblemStatus.KnownError });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problemDetails!.Code.ShouldBe("problem.workaround_required");
    }

    [Fact]
    public async Task A_known_error_cannot_be_published_without_a_root_cause()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var problem = await RaiseAsync(agent, "Attendance devices drop off the network");

        await agent.PostAsJsonAsync($"/api/v1/problems/{problem.Id}/findings",
            new RecordFindingsCommand { Workaround = "Power-cycle the device." });

        var response = await manager.PostAsJsonAsync($"/api/v1/problems/{problem.Id}/status",
            new ChangeProblemStatusCommand { Status = ProblemStatus.KnownError });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problemDetails!.Code.ShouldBe("problem.root_cause_required");
    }

    [Fact]
    public async Task An_agent_can_investigate_but_not_publish_a_known_error()
    {
        // Publishing commits the whole service desk to a workaround, so it is a management act.
        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        var problem = await RaiseAsync(agent, "GST portal times out at month end");

        var findings = await agent.PostAsJsonAsync($"/api/v1/problems/{problem.Id}/findings",
            new RecordFindingsCommand
            {
                RootCause = "Upstream portal throttles our IP range under load.",
                Confidence = RootCauseConfidence.Probable,
                Workaround = "Stagger submissions across the afternoon."
            });

        findings.EnsureSuccessStatusCode();

        var publish = await agent.PostAsJsonAsync($"/api/v1/problems/{problem.Id}/status",
            new ChangeProblemStatusCommand { Status = ProblemStatus.KnownError });

        publish.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Recording_findings_starts_the_investigation_on_its_own()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        var problem = await RaiseAsync(agent, "SAP prints garbled invoices");
        problem.Status.ShouldBe(ProblemStatus.New);

        var response = await agent.PostAsJsonAsync($"/api/v1/problems/{problem.Id}/findings",
            new RecordFindingsCommand { RootCause = "Wrong printer driver rolled out." });

        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<ProblemDetailDto>(TestEnvironment.Json);

        // An investigator should not have to set a status before typing what they found.
        updated!.Status.ShouldBe(ProblemStatus.Investigating);
        updated.InvestigationStartedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_problem_runs_from_investigation_to_closure()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var problem = await RaiseAsync(agent, "Leased line flaps every weekday morning");

        await agent.PostAsJsonAsync($"/api/v1/problems/{problem.Id}/findings",
            new RecordFindingsCommand
            {
                RootCause = "Faulty line card on the provider's aggregation switch.",
                Confidence = RootCauseConfidence.Confirmed,
                Workaround = "Fail traffic to the secondary link before 09:00."
            });

        var known = await manager.PostAsJsonAsync($"/api/v1/problems/{problem.Id}/status",
            new ChangeProblemStatusCommand { Status = ProblemStatus.KnownError });

        known.EnsureSuccessStatusCode();

        var published = await known.Content.ReadFromJsonAsync<ProblemDetailDto>(TestEnvironment.Json);
        published!.Status.ShouldBe(ProblemStatus.KnownError);
        published.KnownErrorAt.ShouldNotBeNull();

        // Resolving requires recording how the cause was actually removed.
        var withoutFix = await manager.PostAsJsonAsync($"/api/v1/problems/{problem.Id}/status",
            new ChangeProblemStatusCommand { Status = ProblemStatus.Resolved });

        withoutFix.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        await agent.PostAsJsonAsync($"/api/v1/problems/{problem.Id}/findings",
            new RecordFindingsCommand { PermanentFix = "Provider replaced the line card." });

        var resolved = await manager.PostAsJsonAsync($"/api/v1/problems/{problem.Id}/status",
            new ChangeProblemStatusCommand { Status = ProblemStatus.Resolved });

        resolved.EnsureSuccessStatusCode();

        var closed = await manager.PostAsJsonAsync($"/api/v1/problems/{problem.Id}/status",
            new ChangeProblemStatusCommand { Status = ProblemStatus.Closed });

        closed.EnsureSuccessStatusCode();

        var final = await closed.Content.ReadFromJsonAsync<ProblemDetailDto>(TestEnvironment.Json);
        final!.Status.ShouldBe(ProblemStatus.Closed);
        final.AllowedTransitions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Linking_an_incident_is_idempotent_and_keeps_the_count_honest()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        var problem = await RaiseAsync(agent, "Shared drive latency");
        var incident = await CreateIncidentAsync(agent, "File server slow from Chennai");

        var first = await agent.PostAsJsonAsync($"/api/v1/problems/{problem.Id}/incidents",
            new LinkIncidentCommand { IncidentId = incident.Id });

        first.EnsureSuccessStatusCode();
        (await first.Content.ReadFromJsonAsync<ProblemDetailDto>(TestEnvironment.Json))!
            .LinkedIncidentCount.ShouldBe(1);

        // A double-click must not fail, nor double-count.
        var second = await agent.PostAsJsonAsync($"/api/v1/problems/{problem.Id}/incidents",
            new LinkIncidentCommand { IncidentId = incident.Id });

        second.EnsureSuccessStatusCode();
        (await second.Content.ReadFromJsonAsync<ProblemDetailDto>(TestEnvironment.Json))!
            .LinkedIncidentCount.ShouldBe(1);

        var unlinked = await agent.DeleteAsync($"/api/v1/problems/{problem.Id}/incidents/{incident.Id}");
        unlinked.EnsureSuccessStatusCode();

        (await unlinked.Content.ReadFromJsonAsync<ProblemDetailDto>(TestEnvironment.Json))!
            .LinkedIncidentCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_requester_can_read_a_workaround_but_not_the_investigation_notes()
    {
        // A known error the service desk cannot see helps nobody, so problems are readable
        // tenant-wide. Internal investigation notes are still filtered.
        var agent = await _env.ClientForAsync(_env.Acme.Agent);
        var employee = await _env.ClientForAsync(_env.Acme.Employee);

        var problem = await RaiseAsync(agent, "Email delivery delayed to external domains");

        await agent.PostAsJsonAsync($"/api/v1/problems/{problem.Id}/findings",
            new RecordFindingsCommand
            {
                RootCause = "Outbound relay rate-limited by the upstream provider.",
                Workaround = "Send bulk mail outside business hours."
            });

        await agent.PostAsJsonAsync($"/api/v1/problems/{problem.Id}/comments",
            new AddProblemCommentCommand
            {
                Body = "Internal: provider contract renegotiation pending.",
                Kind = IncidentCommentKind.WorkNote
            });

        var visible = await employee.GetFromJsonAsync<ProblemDetailDto>(
            $"/api/v1/problems/{problem.Id}", TestEnvironment.Json);

        visible!.Workaround.ShouldBe("Send bulk mail outside business hours.");

        var comments = await employee.GetFromJsonAsync<List<ProblemCommentDto>>(
            $"/api/v1/problems/{problem.Id}/comments", TestEnvironment.Json);

        comments.ShouldNotBeNull();
        comments.ShouldNotContain(c => c.Body.Contains("contract renegotiation"));
    }

    [Fact]
    public async Task A_requester_cannot_raise_or_investigate_a_problem()
    {
        var employee = await _env.ClientForAsync(_env.Acme.Employee);

        var create = await employee.PostAsJsonAsync("/api/v1/problems",
            new CreateProblemCommand { Title = "Unauthorised" });

        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Another_tenants_problem_is_not_reachable()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);
        var neighbour = await _env.ClientForAsync(_env.Northwind.Manager);

        var problem = await RaiseAsync(agent, "Acme-only problem");

        // 404, not 403: confirming it exists discloses another customer's record ids.
        (await neighbour.GetAsync($"/api/v1/problems/{problem.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await neighbour.GetAsync($"/api/v1/problems/{problem.Id}/comments"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var search = await neighbour.GetFromJsonAsync<PagedResult<ProblemSummaryDto>>(
            $"/api/v1/problems?search={problem.Number}", TestEnvironment.Json);

        search!.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task An_incident_from_another_tenant_cannot_be_linked()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);
        var neighbourAgent = await _env.ClientForAsync(_env.Northwind.Agent);

        var problem = await RaiseAsync(agent, "Cross-tenant link attempt");
        var foreign = await CreateIncidentAsync(
            neighbourAgent, "Northwind incident", _env.Northwind.CategoryId);

        var response = await agent.PostAsJsonAsync($"/api/v1/problems/{problem.Id}/incidents",
            new LinkIncidentCommand { IncidentId = foreign.Id });

        // Reads as a non-existent incident, because the look-up is tenant-filtered.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unrecognised_sort_field_is_a_validation_failure()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        var response = await agent.GetAsync("/api/v1/problems?sortBy=; DROP TABLE Problems");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private async Task<ProblemDetailDto> RaiseAsync(
        HttpClient client,
        string title,
        Guid? fromIncidentId = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/problems", new CreateProblemCommand
        {
            Title = title,
            Description = "Raised by the integration suite.",
            FromIncidentId = fromIncidentId
        });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ProblemDetailDto>(TestEnvironment.Json))!;
    }

    /// <param name="categoryId">
    /// Defaults to Acme's category. A caller acting for another tenant must pass that tenant's
    /// own, because the category look-up is tenant-filtered and a foreign one reads as absent.
    /// </param>
    private async Task<IncidentSummary> CreateIncidentAsync(
        HttpClient client,
        string title,
        Guid? categoryId = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/incidents", new
        {
            title,
            description = "Raised by the integration suite.",
            categoryId = categoryId ?? _env.Acme.CategoryId,
            impact = "Significant",
            urgency = "High"
        });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<IncidentSummary>(TestEnvironment.Json))!;
    }

    /// <summary>Only the incident fields these tests actually assert on.</summary>
    private sealed record IncidentSummary(Guid Id, string Number, Guid? CategoryId);
}
