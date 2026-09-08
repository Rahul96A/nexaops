using NexaOps.Domain.Changes;
using NexaOps.Domain.Common;

namespace NexaOps.Domain.Tests.Changes;

/// <summary>
/// The rules governing how a change may proceed.
/// <para>
/// The ones that matter: review is not optional, a risky change needs a way back, and the three
/// change types genuinely differ in the scrutiny they attract.
/// </para>
/// </summary>
public sealed class ChangeStateMachineTests
{
    [Theory]
    [InlineData(ChangeStatus.Draft, ChangeStatus.Assessing)]
    [InlineData(ChangeStatus.Draft, ChangeStatus.Scheduled)]
    [InlineData(ChangeStatus.Assessing, ChangeStatus.AwaitingApproval)]
    [InlineData(ChangeStatus.AwaitingApproval, ChangeStatus.Scheduled)]
    [InlineData(ChangeStatus.AwaitingApproval, ChangeStatus.Rejected)]
    [InlineData(ChangeStatus.Scheduled, ChangeStatus.Implementing)]
    [InlineData(ChangeStatus.Implementing, ChangeStatus.Review)]
    [InlineData(ChangeStatus.Review, ChangeStatus.Closed)]
    public void Legal_transitions_are_permitted(ChangeStatus from, ChangeStatus to)
        => ChangeStateMachine.CanTransition(from, to).ShouldBeTrue();

    [Fact]
    public void An_implemented_change_cannot_skip_its_review()
    {
        // Allowing this would make the outcome field, and therefore change success reporting,
        // a count of records rather than a measure of anything.
        ChangeStateMachine.CanTransition(ChangeStatus.Implementing, ChangeStatus.Closed)
            .ShouldBeFalse();

        ChangeStateMachine.AllowedTransitionsFrom(ChangeStatus.Review)
            .ShouldBe([ChangeStatus.Closed]);
    }

    [Theory]
    [InlineData(ChangeStatus.Closed)]
    [InlineData(ChangeStatus.Rejected)]
    [InlineData(ChangeStatus.Cancelled)]
    public void Terminal_states_have_no_way_out(ChangeStatus terminal)
    {
        ChangeStateMachine.AllowedTransitionsFrom(terminal).ShouldBeEmpty();
        ChangeStateMachine.IsTerminal(terminal).ShouldBeTrue();
    }

    [Fact]
    public void A_rejected_change_cannot_be_resubmitted()
        => ChangeStateMachine.CanTransition(ChangeStatus.Rejected, ChangeStatus.AwaitingApproval)
            .ShouldBeFalse();

    [Fact]
    public void A_scheduled_change_can_go_back_for_reassessment()
        => ChangeStateMachine.CanTransition(ChangeStatus.Scheduled, ChangeStatus.Assessing)
            .ShouldBeTrue();

    [Fact]
    public void Only_a_normal_change_needs_approval_before_it_is_scheduled()
    {
        // A standard change was approved once, when its procedure was accepted; asking again is
        // theatre. An emergency change cannot wait, so its approval is recorded afterwards.
        ChangeStateMachine.RequiresApprovalBeforeScheduling(ChangeType.Normal).ShouldBeTrue();
        ChangeStateMachine.RequiresApprovalBeforeScheduling(ChangeType.Standard).ShouldBeFalse();
        ChangeStateMachine.RequiresApprovalBeforeScheduling(ChangeType.Emergency).ShouldBeFalse();

        ChangeStateMachine.RequiresRetrospectiveApproval(ChangeType.Emergency).ShouldBeTrue();
        ChangeStateMachine.RequiresRetrospectiveApproval(ChangeType.Standard).ShouldBeFalse();
    }

    [Fact]
    public void An_illegal_transition_names_the_move_that_was_refused()
    {
        var error = Should.Throw<DomainException>(
            () => ChangeStateMachine.EnsureCanTransition(ChangeStatus.Implementing, ChangeStatus.Closed));

        error.Code.ShouldBe("change.invalid_transition");
        error.Message.ShouldContain("Implementing");
        error.Message.ShouldContain("Closed");
    }
}

/// <summary>The evidence each stage of a change requires before it can be claimed.</summary>
public sealed class ChangeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.NewGuid();

    private static Change Planned(ChangeRisk risk = ChangeRisk.Medium) => new()
    {
        TenantId = Guid.NewGuid(),
        Number = "CHG0000042",
        Title = "Upgrade the core switch firmware",
        Risk = risk,
        Status = ChangeStatus.Draft,
        ImplementationPlan = "Fail traffic to the secondary, upgrade, fail back.",
        RollbackPlan = "Reflash the previous image from the console.",
        TestPlan = "Confirm BGP peering and run a synthetic transaction.",
        PlannedStartAt = Now.AddDays(2),
        PlannedEndAt = Now.AddDays(2).AddHours(3)
    };

    [Fact]
    public void A_change_cannot_proceed_without_an_implementation_plan()
    {
        var change = Planned();
        change.ImplementationPlan = null;

        Should.Throw<DomainException>(
                () => change.TransitionTo(ChangeStatus.AwaitingApproval, Actor, Now))
            .Code.ShouldBe("change.implementation_plan_required");
    }

    [Fact]
    public void Anything_above_low_risk_needs_a_way_back()
    {
        // A change nobody knows how to back out turns a bad hour into a bad week.
        var change = Planned(ChangeRisk.High);
        change.RollbackPlan = null;

        var error = Should.Throw<DomainException>(
            () => change.TransitionTo(ChangeStatus.Scheduled, Actor, Now));

        error.Code.ShouldBe("change.rollback_plan_required");
        error.Message.ShouldContain("High");
    }

    [Fact]
    public void A_low_risk_change_is_exempt_from_the_rollback_requirement()
    {
        // The ceremony would outweigh the exposure.
        var change = Planned(ChangeRisk.Low);
        change.RollbackPlan = null;

        Should.NotThrow(() => change.TransitionTo(ChangeStatus.Scheduled, Actor, Now));
    }

    [Fact]
    public void Scheduling_requires_a_booked_window()
    {
        var change = Planned();
        change.PlannedStartAt = null;
        change.PlannedEndAt = null;

        Should.Throw<DomainException>(() => change.TransitionTo(ChangeStatus.Scheduled, Actor, Now))
            .Code.ShouldBe("change.window_required");
    }

    [Fact]
    public void A_window_must_end_after_it_starts()
    {
        var change = Planned();

        Should.Throw<DomainException>(() => change.Schedule(Now.AddHours(3), Now.AddHours(1)))
            .Code.ShouldBe("change.invalid_window");

        change.Schedule(Now.AddHours(1), Now.AddHours(3));
        change.PlannedDuration.ShouldBe(TimeSpan.FromHours(2));
        change.IsScheduled.ShouldBeTrue();
    }

    [Fact]
    public void Moving_to_review_requires_a_test_plan()
    {
        var change = Planned();
        change.TestPlan = null;
        change.TransitionTo(ChangeStatus.Scheduled, Actor, Now);
        change.TransitionTo(ChangeStatus.Implementing, Actor, Now);

        Should.Throw<DomainException>(() => change.TransitionTo(ChangeStatus.Review, Actor, Now))
            .Code.ShouldBe("change.test_plan_required");
    }

    [Fact]
    public void A_change_cannot_be_closed_without_an_outcome()
    {
        var change = Planned();
        change.TransitionTo(ChangeStatus.Scheduled, Actor, Now);
        change.TransitionTo(ChangeStatus.Implementing, Actor, Now);
        change.TransitionTo(ChangeStatus.Review, Actor, Now);

        Should.Throw<DomainException>(() => change.TransitionTo(ChangeStatus.Closed, Actor, Now))
            .Code.ShouldBe("change.outcome_required");
    }

    [Fact]
    public void A_review_requires_a_note_whatever_the_outcome()
    {
        // A failed change with no explanation teaches nobody anything, and a successful one with
        // no note is indistinguishable from one that was never reviewed.
        var change = Planned();

        Should.Throw<DomainException>(
                () => change.RecordReview(ChangeOutcome.Successful, "  ", Actor, Now))
            .Code.ShouldBe("change.review_notes_required");
    }

    [Fact]
    public void A_reviewed_change_closes_and_stamps_who_reviewed_it()
    {
        var change = Planned();
        change.TransitionTo(ChangeStatus.Scheduled, Actor, Now);
        change.TransitionTo(ChangeStatus.Implementing, Actor, Now.AddDays(2));
        change.TransitionTo(ChangeStatus.Review, Actor, Now.AddDays(2).AddHours(2));

        change.RecordReview(
            ChangeOutcome.SuccessfulWithIssues,
            "Completed 20 minutes over the window; no customer impact.",
            Actor,
            Now.AddDays(2).AddHours(3));

        change.TransitionTo(ChangeStatus.Closed, Actor, Now.AddDays(3));

        change.Status.ShouldBe(ChangeStatus.Closed);
        change.Outcome.ShouldBe(ChangeOutcome.SuccessfulWithIssues);
        change.ReviewedByUserId.ShouldBe(Actor);
        change.ActualStartAt.ShouldBe(Now.AddDays(2));
        change.ActualEndAt.ShouldBe(Now.AddDays(2).AddHours(2));
    }

    [Fact]
    public void Rejecting_requires_a_reason_and_ends_the_change()
    {
        var change = Planned();
        change.TransitionTo(ChangeStatus.AwaitingApproval, Actor, Now);

        Should.Throw<DomainException>(() => change.RecordRejected("  ", Actor, Now))
            .Code.ShouldBe("change.rejection_reason_required");

        change.RecordRejected("Collides with the quarter-end freeze.", Actor, Now);

        change.Status.ShouldBe(ChangeStatus.Rejected);
        change.RejectionReason.ShouldBe("Collides with the quarter-end freeze.");
    }

    [Fact]
    public void Overlapping_windows_are_detected_so_a_collision_can_be_warned_about()
    {
        var change = Planned();

        change.OverlapsWindowOf(Now.AddDays(2).AddHours(1), Now.AddDays(2).AddHours(4))
            .ShouldBeTrue();

        change.OverlapsWindowOf(Now.AddDays(3), Now.AddDays(3).AddHours(2))
            .ShouldBeFalse();

        // Touching windows do not overlap: one ends exactly as the other begins.
        change.OverlapsWindowOf(Now.AddDays(2).AddHours(3), Now.AddDays(2).AddHours(5))
            .ShouldBeFalse();
    }

    [Fact]
    public void An_unscheduled_change_overlaps_nothing()
    {
        var change = Planned();
        change.PlannedStartAt = null;

        change.OverlapsWindowOf(Now, Now.AddDays(10)).ShouldBeFalse();
    }
}
