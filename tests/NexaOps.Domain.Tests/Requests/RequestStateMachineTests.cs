using NexaOps.Domain.Common;
using NexaOps.Domain.Requests;

namespace NexaOps.Domain.Tests.Requests;

/// <summary>
/// The rules governing how a service request may move through its lifecycle.
/// <para>
/// The commercially meaningful rules are that work cannot start before authorisation, that a
/// rejection is final, and that approval time is not charged against the fulfilment commitment.
/// </para>
/// </summary>
public sealed class RequestStateMachineTests
{
    [Theory]
    [InlineData(RequestStatus.Draft, RequestStatus.AwaitingApproval)]
    [InlineData(RequestStatus.Draft, RequestStatus.Approved)]
    [InlineData(RequestStatus.AwaitingApproval, RequestStatus.Approved)]
    [InlineData(RequestStatus.AwaitingApproval, RequestStatus.Rejected)]
    [InlineData(RequestStatus.Approved, RequestStatus.InProgress)]
    [InlineData(RequestStatus.InProgress, RequestStatus.Pending)]
    [InlineData(RequestStatus.Pending, RequestStatus.InProgress)]
    [InlineData(RequestStatus.InProgress, RequestStatus.Fulfilled)]
    [InlineData(RequestStatus.Fulfilled, RequestStatus.Closed)]
    public void Legal_transitions_are_permitted(RequestStatus from, RequestStatus to)
        => RequestStateMachine.CanTransition(from, to).ShouldBeTrue();

    [Theory]
    [InlineData(RequestStatus.Draft, RequestStatus.InProgress)]
    [InlineData(RequestStatus.Draft, RequestStatus.Fulfilled)]
    [InlineData(RequestStatus.AwaitingApproval, RequestStatus.InProgress)]
    [InlineData(RequestStatus.AwaitingApproval, RequestStatus.Fulfilled)]
    public void Work_cannot_start_before_the_request_is_authorised(RequestStatus from, RequestStatus to)
        => RequestStateMachine.CanTransition(from, to).ShouldBeFalse();

    [Theory]
    [InlineData(RequestStatus.Rejected)]
    [InlineData(RequestStatus.Closed)]
    [InlineData(RequestStatus.Cancelled)]
    public void Terminal_states_have_no_way_out(RequestStatus terminal)
    {
        RequestStateMachine.AllowedTransitionsFrom(terminal).ShouldBeEmpty();
        RequestStateMachine.IsTerminal(terminal).ShouldBeTrue();
    }

    [Fact]
    public void A_rejected_request_cannot_be_resubmitted()
    {
        // Re-submitting would let a refused request quietly become an approved one, and would
        // make approval cycle-time reporting meaningless. A new request is the correct route.
        RequestStateMachine.CanTransition(RequestStatus.Rejected, RequestStatus.AwaitingApproval)
            .ShouldBeFalse();

        RequestStateMachine.CanTransition(RequestStatus.Rejected, RequestStatus.Approved)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_fulfilled_request_can_be_reopened_while_the_requester_confirms()
        => RequestStateMachine.CanTransition(RequestStatus.Fulfilled, RequestStatus.InProgress)
            .ShouldBeTrue();

    [Fact]
    public void An_illegal_transition_reports_which_move_was_refused()
    {
        var error = Should.Throw<DomainException>(
            () => RequestStateMachine.EnsureCanTransition(RequestStatus.Draft, RequestStatus.Fulfilled));

        error.Code.ShouldBe("request.invalid_transition");
        error.Message.ShouldContain("Draft");
        error.Message.ShouldContain("Fulfilled");
    }

    [Fact]
    public void Transitioning_to_the_same_status_is_a_no_op_rather_than_an_error()
        => RequestStateMachine.CanTransition(RequestStatus.InProgress, RequestStatus.InProgress)
            .ShouldBeTrue();

    [Theory]
    [InlineData(RequestStatus.Draft)]
    [InlineData(RequestStatus.AwaitingApproval)]
    [InlineData(RequestStatus.Approved)]
    [InlineData(RequestStatus.InProgress)]
    [InlineData(RequestStatus.Pending)]
    public void Open_states_count_as_outstanding_work(RequestStatus status)
        => RequestStateMachine.IsOpen(status).ShouldBeTrue();

    [Theory]
    [InlineData(RequestStatus.Fulfilled)]
    [InlineData(RequestStatus.Closed)]
    [InlineData(RequestStatus.Rejected)]
    [InlineData(RequestStatus.Cancelled)]
    public void Settled_states_do_not_count_as_outstanding_work(RequestStatus status)
        => RequestStateMachine.IsOpen(status).ShouldBeFalse();

    [Fact]
    public void Approval_time_does_not_run_against_the_fulfilment_commitment()
    {
        // The service desk cannot start work it has not been authorised to do. Charging a slow
        // approver's delay to the desk's fulfilment SLA would measure the wrong team.
        RequestStateMachine.PausesSla(RequestStatus.AwaitingApproval).ShouldBeTrue();
        RequestStateMachine.PausesSla(RequestStatus.Draft).ShouldBeTrue();
        RequestStateMachine.PausesSla(RequestStatus.Pending).ShouldBeTrue();
    }

    [Fact]
    public void The_clock_runs_once_work_can_actually_begin()
    {
        RequestStateMachine.PausesSla(RequestStatus.Approved).ShouldBeFalse();
        RequestStateMachine.PausesSla(RequestStatus.InProgress).ShouldBeFalse();
    }
}
