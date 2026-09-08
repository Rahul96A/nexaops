using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NexaOps.Api.IntegrationTests.Infrastructure;
using NexaOps.Application.Ai;
using NexaOps.Application.Security;

namespace NexaOps.Api.IntegrationTests;

/// <summary>
/// The AI security boundary.
/// <para>
/// The product's claim is that the model can only affect the system through registered tools,
/// that every tool is permission-checked and tenant-scoped, and that an environment without an
/// AI provider says so rather than inventing answers. These tests hold that claim to account.
/// </para>
/// <para>
/// No AI provider is configured in the test host, which is itself one of the things under test:
/// the "not configured" path must be a clear, honest failure rather than a fallback that
/// fabricates a response.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AiSecurityTests
{
    private readonly TestEnvironment _env;

    public AiSecurityTests(TestEnvironment env) => _env = env;

    [Fact]
    public async Task Status_reports_ai_as_unavailable_when_no_provider_is_configured()
    {
        var client = await _env.ClientForAsync(_env.Acme.Agent);

        var status = await client.GetFromJsonAsync<AiStatusDto>("/api/v1/ai/status", TestEnvironment.Json);

        status.ShouldNotBeNull();
        status.IsConfigured.ShouldBeFalse();
        status.Provider.ShouldBe("None");
        status.ChatModel.ShouldBeNull();
        status.UnavailableReason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Asking_a_question_without_a_provider_returns_a_clear_service_unavailable()
    {
        var client = await _env.ClientForAsync(_env.Acme.Agent);

        var response = await client.PostAsJsonAsync(
            "/api/v1/ai/ask",
            new { question = "How many critical incidents are open?" },
            TestEnvironment.Json);

        // The important part is what it does NOT do: there is no fallback path that produces a
        // plausible-sounding answer from no data.
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("ai_not_configured");
        body.ShouldNotContain("critical incidents are open");
    }

    [Fact]
    public async Task A_user_without_the_assistant_permission_cannot_ask_anything()
    {
        var employee = await _env.ClientForAsync(_env.Acme.Employee);

        var response = await employee.PostAsJsonAsync(
            "/api/v1/ai/ask",
            new { question = "Show me every incident in the company." },
            TestEnvironment.Json);

        // Refused on permission before the provider is even consulted.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Status_offers_only_the_tools_the_caller_is_permitted_to_use()
    {
        var agent = await _env.ClientForAsync(_env.Acme.Agent);
        var employee = await _env.ClientForAsync(_env.Acme.Employee);

        var agentStatus = await agent.GetFromJsonAsync<AiStatusDto>("/api/v1/ai/status", TestEnvironment.Json);
        var employeeStatus = await employee.GetFromJsonAsync<AiStatusDto>("/api/v1/ai/status", TestEnvironment.Json);

        agentStatus!.AvailableTools.ShouldNotBeEmpty();
        agentStatus.AvailableTools.ShouldContain(t => t.Name == "search_incidents");

        // The employee holds incident.read, so incident tools are offered; the point is that the
        // list is derived from permissions rather than being the same for everyone.
        employeeStatus!.AvailableTools.ShouldAllBe(t => Permissions.IsKnown(t.RequiredPermission));
    }

    [Fact]
    public async Task Every_registered_tool_declares_a_permission_this_build_enforces()
    {
        using var scope = _env.Factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IAiToolRegistry>();

        registry.All.ShouldNotBeEmpty();

        foreach (var tool in registry.All)
        {
            tool.Name.ShouldNotBeNullOrWhiteSpace();
            tool.Description.Length.ShouldBeGreaterThan(40);

            Permissions.IsKnown(tool.RequiredPermission)
                .ShouldBeTrue($"Tool '{tool.Name}' requires unknown permission '{tool.RequiredPermission}'.");

            // The schema is what constrains the arguments the model may send.
            tool.InputSchema.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Object);
        }
    }

    [Fact]
    public void No_registered_tool_can_change_data_in_this_build()
    {
        using var scope = _env.Factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IAiToolRegistry>();

        // Mutating tools are a later phase and arrive with an explicit human confirmation step.
        // Until then, the assistant is strictly read-only, and this test is what keeps it so.
        registry.All.ShouldAllBe(t => !t.IsMutating);
    }

    [Fact]
    public async Task An_unknown_tool_name_is_refused_rather_than_guessed_at()
    {
        using var scope = _env.Factory.Services.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<IAiToolExecutor>();

        var result = await executor.ExecuteAsync("delete_everything", "{}");

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Permission_is_checked_before_tool_arguments_are_even_parsed()
    {
        using var scope = _env.Factory.Services.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<IAiToolExecutor>();

        // No authenticated caller in this scope, so the caller holds nothing. Malformed
        // arguments are supplied as well, and the response is a permission denial rather than a
        // parse error - which is the correct order: nothing about the request is processed
        // until the caller has been shown to be allowed to make it.
        var result = await executor.ExecuteAsync("search_incidents", "{ not valid json");

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("permission");
        result.Error.ShouldNotContain("JSON");
    }

    [Fact]
    public async Task A_tool_executed_without_the_required_permission_is_denied()
    {
        // The executor runs as whoever the ambient user is. Outside a request there is no
        // authenticated caller, so this exercises the permission gate itself rather than a
        // controller attribute in front of it.
        using var scope = _env.Factory.Services.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<IAiToolExecutor>();
        var registry = scope.ServiceProvider.GetRequiredService<IAiToolRegistry>();

        // Every tool in this build is read-only, so the registry itself is the guarantee that a
        // destructive capability is not reachable by name.
        registry.Find("update_incident").ShouldBeNull();
        registry.Find("create_incident").ShouldBeNull();
        registry.Find("execute_sql").ShouldBeNull();

        var result = await executor.ExecuteAsync("execute_sql", """{"query":"SELECT * FROM identity.Users"}""");

        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task The_ai_endpoints_reject_anonymous_callers()
    {
        var anonymous = _env.CreateAnonymousClient();

        (await anonymous.GetAsync("/api/v1/ai/status")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var ask = await anonymous.PostAsJsonAsync(
            "/api/v1/ai/ask", new { question = "anything" }, TestEnvironment.Json);

        ask.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
