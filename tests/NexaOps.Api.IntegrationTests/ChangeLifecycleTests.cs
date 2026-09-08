using System.Net;
using System.Net.Http.Json;
using NexaOps.Api.IntegrationTests.Infrastructure;
using NexaOps.Application.Changes;
using NexaOps.Application.Common;
using NexaOps.Domain.Changes;

namespace NexaOps.Api.IntegrationTests;

/// <summary>
/// Change management end to end, over real HTTP against real SQL Server.
/// <para>
/// The behaviours worth protecting are that review cannot be skipped, that a risky change needs
/// a documented way back, and that the emergency path is gated by its own permission so it does
/// not quietly become the normal one.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChangeLifecycleTests
{
    private readonly TestEnvironment _env;

    public ChangeLifecycleTests(TestEnvironment env) => _env = env;

    [Fact]
    public async Task A_change_cannot_be_scheduled_without_an_implementation_plan()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var change = await RaiseAsync(manager, "Firmware upgrade", implementationPlan: null);

        var response = await manager.PostAsJsonAsync($"/api/v1/changes/{change.Id}/status",
            new ChangeStatusCommand { Status = ChangeStatus.Scheduled });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("change.implementation_plan_required");
    }

    [Fact]
    public async Task Anything_above_low_risk_needs_a_documented_way_back()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var change = await RaiseAsync(manager, "Database migration", risk: ChangeRisk.High, rollbackPlan: null);

        var response = await manager.PostAsJsonAsync($"/api/v1/changes/{change.Id}/status",
            new ChangeStatusCommand { Status = ChangeStatus.Scheduled });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("change.rollback_plan_required");
    }

    [Fact]
    public async Task A_low_risk_change_is_exempt_from_the_rollback_requirement()
    {
        // The ceremony would outweigh the exposure.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var change = await RaiseAsync(manager, "Update a monitoring threshold",
            risk: ChangeRisk.Low, rollbackPlan: null);

        var response = await manager.PostAsJsonAsync($"/api/v1/changes/{change.Id}/status",
            new ChangeStatusCommand { Status = ChangeStatus.Scheduled });

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_window_must_end_after_it_starts()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var change = await RaiseAsync(manager, "Backwards window");

        var response = await manager.PostAsJsonAsync($"/api/v1/changes/{change.Id}/schedule",
            new ScheduleChangeCommand
            {
                PlannedStartAt = DateTimeOffset.UtcNow.AddDays(2).AddHours(4),
                PlannedEndAt = DateTimeOffset.UtcNow.AddDays(2)
            });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("change.invalid_window");
    }

    [Fact]
    public async Task An_implemented_change_cannot_skip_its_review()
    {
        // Allowing it would make change success reporting a count of records rather than a
        // measure of anything.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var change = await RaiseAsync(manager, "Skip the review");

        await manager.PostAsJsonAsync($"/api/v1/changes/{change.Id}/status",
            new ChangeStatusCommand { Status = ChangeStatus.Scheduled });

        await manager.PostAsJsonAsync($"/api/v1/changes/{change.Id}/status",
            new ChangeStatusCommand { Status = ChangeStatus.Implementing });

        var response = await manager.PostAsJsonAsync($"/api/v1/changes/{change.Id}/status",
            new ChangeStatusCommand { Status = ChangeStatus.Closed });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("change.invalid_transition");
    }

    [Fact]
    public async Task A_change_cannot_close_without_a_recorded_outcome()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var change = await RaiseAsync(manager, "No outcome recorded");

        await Advance(manager, change.Id, ChangeStatus.Scheduled, ChangeStatus.Implementing, ChangeStatus.Review);

        var response = await manager.PostAsJsonAsync($"/api/v1/changes/{change.Id}/status",
            new ChangeStatusCommand { Status = ChangeStatus.Closed });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("change.outcome_required");
    }

    [Fact]
    public async Task A_review_requires_a_note_whatever_the_outcome()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var change = await RaiseAsync(manager, "Review without notes");
        await Advance(manager, change.Id, ChangeStatus.Scheduled, ChangeStatus.Implementing, ChangeStatus.Review);

        var response = await manager.PostAsJsonAsync($"/api/v1/changes/{change.Id}/review",
            new ReviewChangeCommand { Outcome = ChangeOutcome.Successful, Notes = "  " });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("change.review_notes_required");
    }

    [Fact]
    public async Task A_change_runs_from_draft_to_closure_with_its_outcome_recorded()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var change = await RaiseAsync(manager, "Upgrade the core switch firmware");
        await Advance(manager, change.Id, ChangeStatus.Scheduled, ChangeStatus.Implementing, ChangeStatus.Review);

        var reviewed = await manager.PostAsJsonAsync($"/api/v1/changes/{change.Id}/review",
            new ReviewChangeCommand
            {
                Outcome = ChangeOutcome.SuccessfulWithIssues,
                Notes = "Completed 20 minutes over the window; no customer impact."
            });

        reviewed.EnsureSuccessStatusCode();

        var afterReview = await reviewed.Content.ReadFromJsonAsync<ChangeDetailDto>(TestEnvironment.Json);
        afterReview!.Outcome.ShouldBe(ChangeOutcome.SuccessfulWithIssues);
        afterReview.ActualStartAt.ShouldNotBeNull();
        afterReview.ActualEndAt.ShouldNotBeNull();

        var closed = await manager.PostAsJsonAsync($"/api/v1/changes/{change.Id}/status",
            new ChangeStatusCommand { Status = ChangeStatus.Closed });

        closed.EnsureSuccessStatusCode();

        var final = await closed.Content.ReadFromJsonAsync<ChangeDetailDto>(TestEnvironment.Json);
        final!.Status.ShouldBe(ChangeStatus.Closed);
        final.AllowedTransitions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Raising_an_emergency_change_needs_its_own_permission()
    {
        // The control that stops the emergency path becoming the normal one. The service desk
        // manager can raise ordinary changes but not emergency ones.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var response = await manager.PostAsJsonAsync("/api/v1/changes", new CreateChangeCommand
        {
            Title = "Emergency restart of the payment gateway",
            Type = ChangeType.Emergency,
            ImplementationPlan = "Restart the service.",
            RollbackPlan = "Roll back to the previous container revision."
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_standard_change_cannot_be_submitted_to_the_board()
    {
        // It was approved once, when its procedure was accepted. Asking again is theatre.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var change = await RaiseAsync(manager, "Routine certificate renewal", type: ChangeType.Standard);

        var response = await manager.PostAsync($"/api/v1/changes/{change.Id}/submit", null);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("change.approval_not_required");
    }

    [Fact]
    public async Task A_normal_change_routes_to_the_change_advisory_board()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var change = await RaiseAsync(manager, "Normal change needing the board");

        var response = await manager.PostAsync($"/api/v1/changes/{change.Id}/submit", null);
        response.EnsureSuccessStatusCode();

        var submitted = await response.Content.ReadFromJsonAsync<ChangeDetailDto>(TestEnvironment.Json);

        // The board is provisioned as an approval group with no members, so the change waits on
        // it rather than proceeding. Who sits on the board is a customer decision.
        submitted!.Status.ShouldBe(ChangeStatus.AwaitingApproval);
        submitted.Approvals.ShouldHaveSingleItem().ApproverGroupName.ShouldBe("Change Advisory Board");
    }

    [Fact]
    public async Task Colliding_windows_are_surfaced_as_a_warning_not_a_block()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var start = DateTimeOffset.UtcNow.AddDays(30);

        var first = await RaiseAsync(manager, "First change in the window",
            start: start, end: start.AddHours(4));

        await manager.PostAsJsonAsync($"/api/v1/changes/{first.Id}/status",
            new ChangeStatusCommand { Status = ChangeStatus.Scheduled });

        // Scheduling into an occupied window succeeds: two changes at once is sometimes exactly
        // the intent, and the person scheduling is better placed to judge than a rule is.
        var second = await RaiseAsync(manager, "Second change in the same window",
            start: start.AddHours(1), end: start.AddHours(5));

        var detail = await manager.GetFromJsonAsync<ChangeDetailDto>(
            $"/api/v1/changes/{second.Id}", TestEnvironment.Json);

        detail!.CollidingChanges.ShouldContain(c => c.Id == first.Id);
    }

    [Fact]
    public async Task Another_tenants_change_is_not_reachable()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var neighbour = await _env.ClientForAsync(_env.Northwind.Manager);

        var change = await RaiseAsync(manager, "Acme-only change");

        (await neighbour.GetAsync($"/api/v1/changes/{change.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await neighbour.GetAsync($"/api/v1/changes/{change.Id}/comments"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var search = await neighbour.GetFromJsonAsync<PagedResult<ChangeSummaryDto>>(
            $"/api/v1/changes?search={change.Number}", TestEnvironment.Json);

        search!.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task An_unrecognised_sort_field_is_a_validation_failure()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var response = await manager.GetAsync("/api/v1/changes?sortBy=; DROP TABLE Changes");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static async Task Advance(HttpClient client, Guid id, params ChangeStatus[] statuses)
    {
        foreach (var status in statuses)
        {
            var response = await client.PostAsJsonAsync($"/api/v1/changes/{id}/status",
                new ChangeStatusCommand { Status = status });

            response.EnsureSuccessStatusCode();
        }
    }

    private static async Task<ChangeDetailDto> RaiseAsync(
        HttpClient client,
        string title,
        ChangeType type = ChangeType.Normal,
        ChangeRisk risk = ChangeRisk.Medium,
        string? implementationPlan = "Fail traffic over, apply, fail back.",
        string? rollbackPlan = "Restore the previous image from the console.",
        DateTimeOffset? start = null,
        DateTimeOffset? end = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/changes", new CreateChangeCommand
        {
            Title = title,
            Description = "Raised by the integration suite.",
            Type = type,
            Risk = risk,
            ImplementationPlan = implementationPlan,
            RollbackPlan = rollbackPlan,
            TestPlan = "Confirm the service responds and run a synthetic transaction.",
            PlannedStartAt = start ?? DateTimeOffset.UtcNow.AddDays(2),
            PlannedEndAt = end ?? DateTimeOffset.UtcNow.AddDays(2).AddHours(3)
        });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ChangeDetailDto>(TestEnvironment.Json))!;
    }
}
