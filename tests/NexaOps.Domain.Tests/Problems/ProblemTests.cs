using NexaOps.Domain.Common;
using NexaOps.Domain.Problems;

namespace NexaOps.Domain.Tests.Problems;

/// <summary>
/// The rules governing problem investigation.
/// <para>
/// The ones that matter are that a known error cannot be published without something the service
/// desk can actually use, and that a known error is a resting state rather than a waypoint.
/// </para>
/// </summary>
public sealed class ProblemStateMachineTests
{
    [Theory]
    [InlineData(ProblemStatus.New, ProblemStatus.Investigating)]
    [InlineData(ProblemStatus.New, ProblemStatus.KnownError)]
    [InlineData(ProblemStatus.Investigating, ProblemStatus.KnownError)]
    [InlineData(ProblemStatus.Investigating, ProblemStatus.Resolved)]
    [InlineData(ProblemStatus.KnownError, ProblemStatus.FixInProgress)]
    [InlineData(ProblemStatus.KnownError, ProblemStatus.Investigating)]
    [InlineData(ProblemStatus.FixInProgress, ProblemStatus.Resolved)]
    [InlineData(ProblemStatus.Resolved, ProblemStatus.Closed)]
    [InlineData(ProblemStatus.Resolved, ProblemStatus.Investigating)]
    public void Legal_transitions_are_permitted(ProblemStatus from, ProblemStatus to)
        => ProblemStateMachine.CanTransition(from, to).ShouldBeTrue();

    [Theory]
    [InlineData(ProblemStatus.New, ProblemStatus.Resolved)]
    [InlineData(ProblemStatus.New, ProblemStatus.FixInProgress)]
    [InlineData(ProblemStatus.New, ProblemStatus.Closed)]
    public void A_problem_cannot_jump_straight_to_a_conclusion(ProblemStatus from, ProblemStatus to)
        => ProblemStateMachine.CanTransition(from, to).ShouldBeFalse();

    [Theory]
    [InlineData(ProblemStatus.Closed)]
    [InlineData(ProblemStatus.Cancelled)]
    public void Terminal_states_have_no_way_out(ProblemStatus terminal)
    {
        ProblemStateMachine.AllowedTransitionsFrom(terminal).ShouldBeEmpty();
        ProblemStateMachine.IsTerminal(terminal).ShouldBeTrue();
    }

    [Fact]
    public void A_known_error_still_counts_as_open_work()
    {
        // A published workaround is not a solved problem. Treating it as closed would hide a
        // backlog of permanent fixes nobody is funding.
        ProblemStateMachine.IsOpen(ProblemStatus.KnownError).ShouldBeTrue();
        ProblemStateMachine.HasPublishedWorkaround(ProblemStatus.KnownError).ShouldBeTrue();
    }

    [Fact]
    public void A_known_error_can_go_back_to_investigation_when_the_workaround_stops_working()
        => ProblemStateMachine.CanTransition(ProblemStatus.KnownError, ProblemStatus.Investigating)
            .ShouldBeTrue();

    [Fact]
    public void A_resolved_problem_can_be_reopened_when_the_cause_turns_out_to_remain()
        => ProblemStateMachine.CanTransition(ProblemStatus.Resolved, ProblemStatus.Investigating)
            .ShouldBeTrue();

    [Fact]
    public void An_illegal_transition_names_the_move_that_was_refused()
    {
        var error = Should.Throw<DomainException>(
            () => ProblemStateMachine.EnsureCanTransition(ProblemStatus.New, ProblemStatus.Closed));

        error.Code.ShouldBe("problem.invalid_transition");
        error.Message.ShouldContain("New");
        error.Message.ShouldContain("Closed");
    }
}

/// <summary>The evidence each stage of a problem requires before it can be claimed.</summary>
public sealed class ProblemTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.NewGuid();

    private static Problem NewProblem() => new()
    {
        TenantId = Guid.NewGuid(),
        Number = "PRB0000042",
        Title = "Branch VPN drops every weekday at 09:15",
        Status = ProblemStatus.New
    };

    [Fact]
    public void A_known_error_cannot_be_published_without_a_workaround()
    {
        // Publishing one tells the service desk there is something they can do. Without a
        // workaround that promise is false.
        var problem = NewProblem();
        problem.RecordFindings("BGP session flaps on the primary link.", RootCauseConfidence.Probable, null);

        var error = Should.Throw<DomainException>(
            () => problem.TransitionTo(ProblemStatus.KnownError, Actor, Now));

        error.Code.ShouldBe("problem.workaround_required");
        problem.Status.ShouldBe(ProblemStatus.New);
    }

    [Fact]
    public void A_known_error_cannot_be_published_without_a_root_cause()
    {
        var problem = NewProblem();
        problem.RecordFindings(null, null, "Fail traffic over to the secondary link.");

        Should.Throw<DomainException>(() => problem.TransitionTo(ProblemStatus.KnownError, Actor, Now))
            .Code.ShouldBe("problem.root_cause_required");
    }

    [Fact]
    public void A_known_error_with_both_publishes_and_stamps_the_date()
    {
        var problem = NewProblem();
        problem.RecordFindings(
            "BGP session flaps on the primary link.",
            RootCauseConfidence.Confirmed,
            "Fail traffic over to the secondary link.");

        problem.TransitionTo(ProblemStatus.KnownError, Actor, Now);

        problem.Status.ShouldBe(ProblemStatus.KnownError);
        problem.KnownErrorAt.ShouldBe(Now);
        problem.InvestigationStartedAt.ShouldBe(Now);
        problem.HasWorkaround.ShouldBeTrue();
    }

    [Fact]
    public void Resolving_requires_recording_how_the_cause_was_removed()
    {
        var problem = NewProblem();
        problem.TransitionTo(ProblemStatus.Investigating, Actor, Now);

        Should.Throw<DomainException>(() => problem.TransitionTo(ProblemStatus.Resolved, Actor, Now))
            .Code.ShouldBe("problem.permanent_fix_required");

        problem.PermanentFix = "Replaced the failing line card and re-established the BGP peering.";
        problem.TransitionTo(ProblemStatus.Resolved, Actor, Now.AddDays(3));

        problem.Status.ShouldBe(ProblemStatus.Resolved);
        problem.ResolvedAt.ShouldBe(Now.AddDays(3));
        problem.ResolvedByUserId.ShouldBe(Actor);
    }

    [Fact]
    public void Recording_a_root_cause_without_a_stated_confidence_defaults_to_suspected()
    {
        // "We think it is the database" and "we proved it is the database" justify very
        // different spend, so the weaker reading is the safe default.
        var problem = NewProblem();
        problem.RecordFindings("Possibly the connection pool.", null, null);

        problem.RootCauseConfidence.ShouldBe(RootCauseConfidence.Suspected);
    }

    [Fact]
    public void Recording_findings_leaves_untouched_fields_alone()
    {
        var problem = NewProblem();
        problem.RecordFindings("Cause A", RootCauseConfidence.Confirmed, "Workaround A");

        problem.RecordFindings(null, null, "Workaround B");

        problem.RootCause.ShouldBe("Cause A");
        problem.RootCauseConfidence.ShouldBe(RootCauseConfidence.Confirmed);
        problem.Workaround.ShouldBe("Workaround B");
    }

    [Fact]
    public void Assigning_without_an_owner_keeps_the_existing_owner()
    {
        // An unowned problem is one nobody is answerable for, so reassigning the work must not
        // silently clear accountability for it.
        var problem = NewProblem();
        var owner = Guid.NewGuid();

        problem.Assign(Guid.NewGuid(), Guid.NewGuid(), owner);
        problem.Assign(Guid.NewGuid(), Guid.NewGuid(), null);

        problem.OwnerUserId.ShouldBe(owner);
    }

    [Fact]
    public void Cancelling_records_who_closed_it_and_when()
    {
        var problem = NewProblem();

        problem.TransitionTo(ProblemStatus.Cancelled, Actor, Now);

        problem.ClosedAt.ShouldBe(Now);
        problem.ClosedByUserId.ShouldBe(Actor);
    }
}
