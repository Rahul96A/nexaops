using System.Net;
using System.Net.Http.Json;
using NexaOps.Api.IntegrationTests.Infrastructure;
using NexaOps.Application.Common;
using NexaOps.Application.Incidents;
using NexaOps.Application.Workflows;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Workflows;

namespace NexaOps.Api.IntegrationTests;

/// <summary>
/// The workflow engine end to end, over real HTTP against real SQL Server.
/// <para>
/// The behaviours worth protecting: a rule actually changes the record, a rule that does not
/// match says why, a rule cannot be pointed across a tenant boundary, a failing rule does not
/// fail the user's action, and automation does not trigger automation.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class WorkflowTests
{
    private readonly TestEnvironment _env;

    public WorkflowTests(TestEnvironment env) => _env = env;

    // -----------------------------------------------------------------
    // Authoring
    // -----------------------------------------------------------------

    [Fact]
    public async Task An_agent_can_read_the_run_history_but_not_write_rules()
    {
        // Deliberate split. An agent whose ticket rerouted itself needs to find out why; writing
        // rules routes other people's work and belongs with whoever owns the desk.
        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        (await agent.GetAsync("/api/v1/workflows")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await agent.GetAsync("/api/v1/workflows/runs")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var attempt = await agent.PostAsJsonAsync(
            "/api/v1/workflows",
            NewRule($"Agent attempt {Guid.NewGuid():N}"[..24]),
            TestEnvironment.Json);

        attempt.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_rule_written_against_a_field_the_module_does_not_publish_is_refused()
    {
        // Stored, it would look configured and never match — the worst of both outcomes.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var rule = NewRule($"Bad field {Guid.NewGuid():N}"[..24]);
        rule.Conditions.Add(new UpsertWorkflowConditionCommand
        {
            Field = "FavouriteColour",
            Operator = WorkflowConditionOperator.Equals,
            Value = "blue"
        });

        var response = await manager.PostAsJsonAsync("/api/v1/workflows", rule, TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("FavouriteColour");
    }

    [Fact]
    public async Task A_rule_cannot_be_pointed_at_another_tenants_group()
    {
        // The neighbour's group is real, and the identifier is valid. It has to read as
        // non-existent, not as somebody else's team.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var rule = NewRule($"Cross tenant {Guid.NewGuid():N}"[..24]);
        rule.Actions.Add(new UpsertWorkflowActionCommand
        {
            Sequence = 0,
            Type = WorkflowActionType.AssignToGroup,
            TargetGroupId = _env.Northwind.GroupId
        });

        var response = await manager.PostAsJsonAsync("/api/v1/workflows", rule, TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("does not exist");
    }

    [Fact]
    public async Task A_rules_module_and_trigger_cannot_be_changed_after_it_exists()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var rule = NewRule($"Immutable {Guid.NewGuid():N}"[..24]);
        rule.Actions.Add(NotifyRequester());

        // Inactive: this test is about what the API refuses, and an active rule left behind in a
        // shared database would quietly run against every record the rest of the suite creates.
        rule.IsActive = false;

        var created = await CreateAsync(manager, rule);

        rule.Trigger = WorkflowTrigger.StatusChanged;

        var response = await manager.PutAsJsonAsync(
            $"/api/v1/workflows/{created.Id}", rule, TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("workflow.trigger_immutable");
    }

    [Fact]
    public async Task A_rule_with_no_actions_cannot_be_activated()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var rule = NewRule($"Empty {Guid.NewGuid():N}"[..24]);
        rule.IsActive = false;

        var created = await CreateAsync(manager, rule);

        var response = await manager.PostAsJsonAsync(
            $"/api/v1/workflows/{created.Id}/active",
            new { isActive = true },
            TestEnvironment.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("workflow.no_actions");
    }

    // -----------------------------------------------------------------
    // Execution
    // -----------------------------------------------------------------

    [Fact]
    public async Task A_matching_rule_routes_the_incident_and_records_what_it_did()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var rule = NewRule($"Route majors {Guid.NewGuid():N}"[..24]);
        rule.Conditions.Add(new UpsertWorkflowConditionCommand
        {
            Field = "Priority",
            Operator = WorkflowConditionOperator.LessThan,
            Value = "3"
        });
        rule.Actions.Add(new UpsertWorkflowActionCommand
        {
            Sequence = 0,
            Type = WorkflowActionType.AssignToGroup,
            TargetGroupId = _env.Acme.OtherGroupId
        });

        var created = await CreateAsync(manager, rule);

        try
        {
            // Extensive impact and high urgency resolve to something more urgent than P3.
            var incident = await RaiseAsync(manager, "Payroll unreachable across the branch network", "Extensive", "High");

            ((int)incident.Priority).ShouldBeLessThan((int)Priority.P3Moderate);
            incident.AssignmentGroupId.ShouldBe(_env.Acme.OtherGroupId);

            var runs = await RunsForAsync(manager, incident.Id, created.Id);
            var run = runs.Items.ShouldHaveSingleItem();

            run.Status.ShouldBe(WorkflowRunStatus.Succeeded);
            run.WorkflowName.ShouldBe(rule.Name);
            run.Trigger.ShouldBe(WorkflowTrigger.RecordCreated);

            var step = run.Steps.ShouldHaveSingleItem();
            step.ActionType.ShouldBe(WorkflowActionType.AssignToGroup);
            step.Status.ShouldBe(WorkflowStepStatus.Succeeded);
            step.Error.ShouldBeNull();
        }
        finally
        {
            await DeactivateAsync(manager, created.Id);
        }
    }

    [Fact]
    public async Task A_rule_that_does_not_match_records_why()
    {
        // "Why did my rule not fire" is the only question anybody asks about automation.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var rule = NewRule($"Never matches {Guid.NewGuid():N}"[..24]);
        rule.Conditions.Add(new UpsertWorkflowConditionCommand
        {
            Field = "CategoryName",
            Operator = WorkflowConditionOperator.Equals,
            Value = "A category nobody has"
        });
        rule.Actions.Add(NotifyRequester());

        var created = await CreateAsync(manager, rule);

        try
        {
            var incident = await RaiseAsync(manager, "Ordinary incident that no rule should touch");

            var run = (await RunsForAsync(manager, incident.Id, created.Id)).Items.ShouldHaveSingleItem();

            run.Status.ShouldBe(WorkflowRunStatus.Skipped);
            run.Steps.ShouldBeEmpty();
            run.Outcome.ShouldNotBeNull();
            run.Outcome.ShouldContain("CategoryName");
            run.Outcome.ShouldContain("A category nobody has");
        }
        finally
        {
            await DeactivateAsync(manager, created.Id);
        }
    }

    [Fact]
    public async Task A_rule_may_raise_priority_but_never_lower_it()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var rule = NewRule($"Lower priority {Guid.NewGuid():N}"[..24]);
        rule.Actions.Add(new UpsertWorkflowActionCommand
        {
            Sequence = 0,
            Type = WorkflowActionType.SetPriority,
            TargetPriority = Priority.P5Planning
        });

        var created = await CreateAsync(manager, rule);

        try
        {
            var incident = await RaiseAsync(manager, "Something urgent a rule must not bury", "Extensive", "High");

            // The record keeps the priority the matrix gave it. A rule that can de-prioritise is
            // a rule that can quietly bury somebody's outage.
            ((int)incident.Priority).ShouldBeLessThan((int)Priority.P5Planning);

            var run = (await RunsForAsync(manager, incident.Id, created.Id)).Items.ShouldHaveSingleItem();
            var step = run.Steps.ShouldHaveSingleItem();

            // Skipped rather than failed: the record is already more urgent than the rule wanted,
            // so there is nothing wrong, and nothing to do.
            step.Status.ShouldBe(WorkflowStepStatus.Skipped);
            run.Status.ShouldBe(WorkflowRunStatus.Succeeded);
        }
        finally
        {
            await DeactivateAsync(manager, created.Id);
        }
    }

    [Fact]
    public async Task Automation_does_not_trigger_automation()
    {
        // One rule raises priority on creation; another watches for priority changes. Without a
        // recursion guard the second would run as a consequence of the first, and a pair of
        // rules like this would loop.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var escalate = NewRule($"Escalate on raise {Guid.NewGuid():N}"[..24]);
        escalate.Actions.Add(new UpsertWorkflowActionCommand
        {
            Sequence = 0,
            Type = WorkflowActionType.SetPriority,
            TargetPriority = Priority.P1Critical
        });

        var onPriority = NewRule($"Watch priority {Guid.NewGuid():N}"[..24]);
        onPriority.Trigger = WorkflowTrigger.PriorityChanged;
        onPriority.Actions.Add(NotifyRequester());

        var first = await CreateAsync(manager, escalate);
        var second = await CreateAsync(manager, onPriority);

        try
        {
            var incident = await RaiseAsync(manager, "Incident that two rules both want");

            var runs = (await RunsForAsync(manager, incident.Id)).Items;

            runs.Single(r => r.WorkflowDefinitionId == first.Id).Status
                .ShouldBe(WorkflowRunStatus.Succeeded);

            // The priority rule was reached, and refused, with an explanation rather than
            // silently: an administrator whose rule cannot run deserves to be told.
            var suppressed = runs.SingleOrDefault(r => r.WorkflowDefinitionId == second.Id);
            suppressed.ShouldNotBeNull();
            suppressed.Status.ShouldBe(WorkflowRunStatus.Suppressed);
            suppressed.Outcome.ShouldNotBeNull();
            suppressed.Outcome.ShouldContain("Automation does not trigger automation");
        }
        finally
        {
            await DeactivateAsync(manager, first.Id);
            await DeactivateAsync(manager, second.Id);
        }
    }

    [Fact]
    public async Task A_rule_pointed_at_a_deactivated_person_fails_the_step_not_the_incident()
    {
        // The person a rule assigns to leaves. Every incident raised afterwards must still be
        // raised — the automation is broken, the service desk is not.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var rule = NewRule($"Assign to ghost {Guid.NewGuid():N}"[..24]);
        rule.Actions.Add(new UpsertWorkflowActionCommand
        {
            Sequence = 0,
            Type = WorkflowActionType.AssignToUser,
            TargetUserId = _env.Acme.Agent.Id
        });
        rule.Actions.Add(NotifyRequester());
        rule.Actions[1].Sequence = 1;

        var created = await CreateAsync(manager, rule);

        // Take the target out from under the rule, the way a leaver would.
        await _env.DeactivateUserAsync(_env.Acme.Agent.Id);

        try
        {
            var incident = await RaiseAsync(manager, "Incident raised while the rule's target is gone");

            incident.Id.ShouldNotBe(Guid.Empty);
            incident.AssignedToUserId.ShouldBeNull();

            var run = (await RunsForAsync(manager, incident.Id, created.Id)).Items.ShouldHaveSingleItem();

            // Partially completed, not failed: the notification still went out. Reporting either
            // extreme would be a lie about what happened.
            run.Status.ShouldBe(WorkflowRunStatus.PartiallyCompleted);

            var failed = run.Steps.Single(s => s.ActionType == WorkflowActionType.AssignToUser);
            failed.Status.ShouldBe(WorkflowStepStatus.Failed);
            failed.Error.ShouldNotBeNull();

            run.Steps.Single(s => s.ActionType == WorkflowActionType.NotifyUser)
                .Status.ShouldBe(WorkflowStepStatus.Succeeded);
        }
        finally
        {
            await DeactivateAsync(manager, created.Id);
            await _env.ReactivateUserAsync(_env.Acme.Agent.Id);
        }
    }

    [Fact]
    public async Task A_tenants_rules_do_not_run_against_a_neighbours_records()
    {
        var acme = await _env.ClientForAsync(_env.Acme.Manager);
        var northwind = await _env.ClientForAsync(_env.Northwind.Manager);

        var rule = NewRule($"Acme only {Guid.NewGuid():N}"[..24]);
        rule.Actions.Add(new UpsertWorkflowActionCommand
        {
            Sequence = 0,
            Type = WorkflowActionType.AssignToGroup,
            TargetGroupId = _env.Acme.OtherGroupId
        });

        var created = await CreateAsync(acme, rule);

        try
        {
            var theirs = await RaiseAsync(northwind, "A neighbour's incident");

            // Untouched, and their run history is empty for it.
            theirs.AssignmentGroupId.ShouldNotBe(_env.Acme.OtherGroupId);
            (await RunsForAsync(northwind, theirs.Id)).Items.ShouldBeEmpty();

            // And the neighbour cannot read Acme's rule either.
            (await northwind.GetAsync($"/api/v1/workflows/{created.Id}"))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        finally
        {
            await DeactivateAsync(acme, created.Id);
        }
    }

    [Fact]
    public async Task Rules_run_in_sequence_and_every_matching_rule_runs()
    {
        // Rules are not mutually exclusive: two rules that both match both run, in order.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var routeFirst = NewRule($"First route {Guid.NewGuid():N}"[..24]);
        routeFirst.Sequence = 0;
        routeFirst.Actions.Add(new UpsertWorkflowActionCommand
        {
            Sequence = 0,
            Type = WorkflowActionType.AssignToGroup,
            TargetGroupId = _env.Acme.GroupId
        });

        var routeSecond = NewRule($"Second route {Guid.NewGuid():N}"[..24]);
        routeSecond.Sequence = 1;
        routeSecond.Actions.Add(new UpsertWorkflowActionCommand
        {
            Sequence = 0,
            Type = WorkflowActionType.AssignToGroup,
            TargetGroupId = _env.Acme.OtherGroupId
        });

        var first = await CreateAsync(manager, routeFirst);
        var second = await CreateAsync(manager, routeSecond);

        try
        {
            var incident = await RaiseAsync(manager, "Incident both rules want to route");

            // The later rule wins because it ran last. Predictable from the sequence, which is
            // the whole reason rules carry one.
            incident.AssignmentGroupId.ShouldBe(_env.Acme.OtherGroupId);

            var runs = (await RunsForAsync(manager, incident.Id)).Items;
            runs.ShouldContain(r => r.WorkflowDefinitionId == first.Id);
            runs.ShouldContain(r => r.WorkflowDefinitionId == second.Id);
        }
        finally
        {
            await DeactivateAsync(manager, first.Id);
            await DeactivateAsync(manager, second.Id);
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static UpsertWorkflowCommand NewRule(string name) => new()
    {
        Name = name,
        Description = "Created by the workflow suite.",
        Module = ServiceModule.Incident,
        Trigger = WorkflowTrigger.RecordCreated,
        IsActive = true
    };

    private static UpsertWorkflowActionCommand NotifyRequester() => new()
    {
        Sequence = 0,
        Type = WorkflowActionType.NotifyUser,
        Recipient = WorkflowRecipient.Requester,
        Message = "{number} was picked up by a rule: {title}"
    };

    private static async Task<WorkflowDetailDto> CreateAsync(HttpClient client, UpsertWorkflowCommand rule)
    {
        var response = await client.PostAsJsonAsync("/api/v1/workflows", rule, TestEnvironment.Json);

        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Expected 201 but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<WorkflowDetailDto>(TestEnvironment.Json))!;
    }

    /// <summary>
    /// Switches a rule off at the end of a test.
    /// <para>
    /// The suite shares one database, so a rule left active would silently reroute every
    /// incident raised by every other test class afterwards.
    /// </para>
    /// </summary>
    private static async Task DeactivateAsync(HttpClient client, Guid id)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/workflows/{id}/active", new { isActive = false }, TestEnvironment.Json);

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// The runs recorded against one record, optionally narrowed to one rule.
    /// <para>
    /// Narrowing matters: the suite shares a database, so an assertion over every run against a
    /// record would depend on what other rules happen to exist when the test runs.
    /// </para>
    /// </summary>
    private static async Task<PagedResult<WorkflowRunDto>> RunsForAsync(
        HttpClient client,
        Guid recordId,
        Guid? definitionId = null)
    {
        var url = $"/api/v1/workflows/runs?recordId={recordId}"
                  + (definitionId is null ? string.Empty : $"&workflowDefinitionId={definitionId}");

        return (await client.GetFromJsonAsync<PagedResult<WorkflowRunDto>>(url, TestEnvironment.Json))!;
    }

    private static async Task<IncidentDetailDto> RaiseAsync(
        HttpClient client,
        string title,
        string impact = "Moderate",
        string urgency = "Medium")
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/incidents",
            new
            {
                title,
                description = "Raised by the workflow suite to exercise the engine end to end.",
                impact,
                urgency
            },
            TestEnvironment.Json);

        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Expected 201 but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<IncidentDetailDto>(TestEnvironment.Json))!;
    }
}
