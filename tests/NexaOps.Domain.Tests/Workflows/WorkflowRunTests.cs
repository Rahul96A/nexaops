using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Workflows;

namespace NexaOps.Domain.Tests.Workflows;

/// <summary>
/// How a run settles.
/// <para>
/// The rule that matters: a run where some actions worked and some did not must not report
/// either extreme. "Succeeded" hides a failure an administrator needs to see, and "Failed"
/// suggests nothing happened when the record was in fact rerouted.
/// </para>
/// </summary>
public sealed class WorkflowRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.FromHours(5.5));

    private static WorkflowRun Run(params WorkflowStepStatus[] steps)
    {
        var run = new WorkflowRun
        {
            TenantId = Guid.NewGuid(),
            WorkflowDefinitionId = Guid.NewGuid(),
            WorkflowName = "Route network P1s to Network Operations",
            Module = ServiceModule.Incident,
            RecordId = Guid.NewGuid(),
            RecordNumber = "INC0001234",
            Trigger = WorkflowTrigger.RecordCreated,
            StartedAt = Now
        };

        for (var i = 0; i < steps.Length; i++)
        {
            run.Steps.Add(new WorkflowStepRun
            {
                Sequence = i,
                ActionType = WorkflowActionType.NotifyUser,
                Status = steps[i]
            });
        }

        return run;
    }

    [Fact]
    public void Every_action_succeeding_is_a_success()
    {
        var run = Run(WorkflowStepStatus.Succeeded, WorkflowStepStatus.Succeeded);
        run.Complete(Now.AddSeconds(2));

        run.Status.ShouldBe(WorkflowRunStatus.Succeeded);
        run.Duration.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Every_action_failing_is_a_failure()
    {
        var run = Run(WorkflowStepStatus.Failed, WorkflowStepStatus.Failed);
        run.Complete(Now);

        run.Status.ShouldBe(WorkflowRunStatus.Failed);
    }

    [Fact]
    public void A_mixed_run_is_partially_completed_rather_than_either_extreme()
    {
        var run = Run(WorkflowStepStatus.Succeeded, WorkflowStepStatus.Failed);
        run.Complete(Now);

        run.Status.ShouldBe(WorkflowRunStatus.PartiallyCompleted);
    }

    [Fact]
    public void Skipped_actions_do_not_count_against_a_run()
    {
        // "Notify the assignee" on an unassigned record skipped itself. Nothing went wrong.
        var run = Run(WorkflowStepStatus.Succeeded, WorkflowStepStatus.Skipped);
        run.Complete(Now);

        run.Status.ShouldBe(WorkflowRunStatus.Succeeded);
    }

    [Fact]
    public void A_run_where_everything_skipped_succeeds_and_says_so()
    {
        var run = Run(WorkflowStepStatus.Skipped, WorkflowStepStatus.Skipped);
        run.Complete(Now);

        run.Status.ShouldBe(WorkflowRunStatus.Succeeded);
        run.Outcome.ShouldNotBeNull();
        run.Outcome.ShouldContain("skipped");
    }

    [Fact]
    public void An_unfinished_run_has_no_duration()
    {
        Run(WorkflowStepStatus.Succeeded).Duration.ShouldBeNull();
    }

    [Fact]
    public void A_rule_with_no_actions_is_not_runnable()
    {
        // Storing it active would put a run row against every record that matched, for a rule
        // that by construction cannot do anything.
        var definition = new WorkflowDefinition
        {
            TenantId = Guid.NewGuid(),
            Name = "Does nothing",
            Module = ServiceModule.Incident,
            Trigger = WorkflowTrigger.RecordCreated,
            IsActive = true
        };

        definition.IsRunnable.ShouldBeFalse();

        definition.Actions.Add(new WorkflowAction { Type = WorkflowActionType.NotifyUser });
        definition.IsRunnable.ShouldBeTrue();

        definition.IsActive = false;
        definition.IsRunnable.ShouldBeFalse();
    }

    [Fact]
    public void A_message_renders_the_two_supported_placeholders()
    {
        var action = new WorkflowAction
        {
            Type = WorkflowActionType.NotifyUser,
            Message = "{number} needs attention: {title}"
        };

        action.RenderMessage("INC0001234", "Branch router unreachable")
            .ShouldBe("INC0001234 needs attention: Branch router unreachable");
    }

    [Fact]
    public void An_unset_message_falls_back_to_the_record_title()
    {
        new WorkflowAction { Type = WorkflowActionType.NotifyUser }
            .RenderMessage("INC0001234", "Branch router unreachable")
            .ShouldBe("Branch router unreachable");
    }

    [Fact]
    public void An_unrecognised_placeholder_is_left_alone_rather_than_blanked()
    {
        // There is no template language here on purpose. Anything that is not one of the two
        // supported tokens is literal text, which is predictable; silently blanking it would
        // leave a notification with a hole in it and no way to tell why.
        new WorkflowAction { Type = WorkflowActionType.NotifyUser, Message = "{number} for {assignee}" }
            .RenderMessage("INC0001234", "Anything")
            .ShouldBe("INC0001234 for {assignee}");
    }
}
