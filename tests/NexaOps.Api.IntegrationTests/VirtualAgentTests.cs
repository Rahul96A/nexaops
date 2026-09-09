using System.Net;
using System.Net.Http.Json;
using NexaOps.Api.IntegrationTests.Infrastructure;
using NexaOps.Application.Ai;
using NexaOps.Application.Common;
using NexaOps.Application.Incidents;

namespace NexaOps.Api.IntegrationTests;

/// <summary>
/// The virtual agent end to end, over real HTTP against real SQL Server.
/// <para>
/// The integration suite runs without an AI provider, which is the point of several of these:
/// an environment with no provider must say the agent is unavailable rather than answer from
/// nowhere. The confirmation path is deliberately testable without one, because confirming a
/// proposal calls no model at all — it is the ordinary incident service with the ordinary rules.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class VirtualAgentTests
{
    private readonly TestEnvironment _env;

    public VirtualAgentTests(TestEnvironment env) => _env = env;

    [Fact]
    public async Task With_no_provider_configured_the_agent_says_so_rather_than_answering()
    {
        // The rule that matters most about AI in this product: an unconfigured environment
        // reports itself unavailable. It does not fall back to a canned reply that a demo
        // audience would take for a working agent.
        var employee = await _env.ClientForAsync(_env.Acme.Employee);

        var response = await employee.PostAsJsonAsync(
            "/api/v1/ai/agent/chat",
            new { message = "My VPN will not connect" },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).ShouldContain("ai_not_configured");
    }

    [Fact]
    public async Task Confirming_a_proposal_raises_a_real_incident_owned_by_the_person()
    {
        // No model is involved here, which is the design: the agent suggested wording, the
        // person decided. The record is created through the same service the UI uses, so it
        // carries their identity, their tenant, the priority matrix and the SLA clocks.
        var employee = await _env.ClientForAsync(_env.Acme.Employee);

        var response = await employee.PostAsJsonAsync(
            "/api/v1/ai/agent/confirm",
            new
            {
                kind = "raise_incident",
                title = "Laptop will not charge",
                description = "The charger light does not come on when plugged in.",
                urgency = "High"
            },
            TestEnvironment.Json);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<VirtualAgentActionResultDto>(
            TestEnvironment.Json);

        result.ShouldNotBeNull();
        result.RecordNumber.ShouldStartWith("INC");

        var incident = await employee.GetFromJsonAsync<IncidentDetailDto>(
            $"/api/v1/incidents/{result.RecordId}", TestEnvironment.Json);

        incident.ShouldNotBeNull();
        incident.Title.ShouldBe("Laptop will not charge");
        incident.RequesterId.ShouldBe(_env.Acme.Employee.Id);

        // Raised through a conversation, and the record says so rather than looking like a
        // portal ticket.
        incident.Channel.ShouldBe(NexaOps.Domain.ServiceDesk.IncidentChannel.Chat);
    }

    [Fact]
    public async Task A_confirmation_carries_what_the_person_edited_not_what_was_proposed()
    {
        // There is no server-side proposal store. The fields on the request are the fields that
        // become the incident, so somebody who corrected the agent's wording gets their wording
        // and nothing stale can be resurrected.
        var employee = await _env.ClientForAsync(_env.Acme.Employee);

        var response = await employee.PostAsJsonAsync(
            "/api/v1/ai/agent/confirm",
            new
            {
                kind = "raise_incident",
                title = "Corrected by the person before confirming",
                description = "They rewrote this.",
                urgency = "Low"
            },
            TestEnvironment.Json);

        response.EnsureSuccessStatusCode();

        var result = (await response.Content.ReadFromJsonAsync<VirtualAgentActionResultDto>(
            TestEnvironment.Json))!;

        var incident = await employee.GetFromJsonAsync<IncidentDetailDto>(
            $"/api/v1/incidents/{result.RecordId}", TestEnvironment.Json);

        incident!.Title.ShouldBe("Corrected by the person before confirming");
        incident.Description.ShouldBe("They rewrote this.");
    }

    [Fact]
    public async Task A_confirmation_with_no_title_is_refused()
    {
        var employee = await _env.ClientForAsync(_env.Acme.Employee);

        var response = await employee.PostAsJsonAsync(
            "/api/v1/ai/agent/confirm",
            new { kind = "raise_incident", title = "", description = "Nothing to go on." },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("agent.title_required");
    }

    [Fact]
    public async Task The_agent_can_only_do_the_one_thing_it_is_built_to_do()
    {
        // The action kind is checked against a closed set rather than dispatched dynamically.
        // Anything else — however plausible the name — is refused.
        var employee = await _env.ClientForAsync(_env.Acme.Employee);

        var response = await employee.PostAsJsonAsync(
            "/api/v1/ai/agent/confirm",
            new { kind = "delete_all_incidents", title = "Tidy up" },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("agent.unknown_action");
    }

    [Fact]
    public async Task An_incident_raised_by_the_agent_is_audited_as_an_ai_action()
    {
        // Somebody reviewing how a ticket came to exist should be able to see that it began as a
        // suggestion a person accepted, not as an ordinary portal submission.
        var employee = await _env.ClientForAsync(_env.Acme.Employee);
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var response = await employee.PostAsJsonAsync(
            "/api/v1/ai/agent/confirm",
            new { kind = "raise_incident", title = "Audited agent ticket", urgency = "Medium" },
            TestEnvironment.Json);

        response.EnsureSuccessStatusCode();

        var result = (await response.Content.ReadFromJsonAsync<VirtualAgentActionResultDto>(
            TestEnvironment.Json))!;

        var activity = await manager.GetStringAsync($"/api/v1/audit/Incident/{result.RecordId}");

        activity.ShouldContain("virtual agent");
    }

    [Fact]
    public async Task An_anonymous_caller_reaches_neither_endpoint()
    {
        var anonymous = _env.CreateAnonymousClient();

        (await anonymous.PostAsJsonAsync(
                "/api/v1/ai/agent/chat", new { message = "hello" }, TestEnvironment.Json))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await anonymous.PostAsJsonAsync(
                "/api/v1/ai/agent/confirm",
                new { kind = "raise_incident", title = "x" },
                TestEnvironment.Json))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_incident_raised_by_the_agent_stays_inside_its_tenant()
    {
        var employee = await _env.ClientForAsync(_env.Acme.Employee);
        var neighbour = await _env.ClientForAsync(_env.Northwind.Manager);

        var response = await employee.PostAsJsonAsync(
            "/api/v1/ai/agent/confirm",
            new { kind = "raise_incident", title = "Acme only, raised through the agent" },
            TestEnvironment.Json);

        response.EnsureSuccessStatusCode();

        var result = (await response.Content.ReadFromJsonAsync<VirtualAgentActionResultDto>(
            TestEnvironment.Json))!;

        // The agent is a different way in, not a different set of rules.
        (await neighbour.GetAsync($"/api/v1/incidents/{result.RecordId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
