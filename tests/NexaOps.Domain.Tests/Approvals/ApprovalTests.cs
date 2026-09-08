using NexaOps.Domain.Approvals;
using NexaOps.Domain.Common;

namespace NexaOps.Domain.Tests.Approvals;

/// <summary>
/// How a single approval behaves, and how a stage of them combines into one outcome.
/// <para>
/// The rule that matters is that a recorded decision is never rewritten. An approval trail whose
/// entries can change is not evidence of anything.
/// </para>
/// </summary>
public sealed class ApprovalTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 6, 30, 0, TimeSpan.Zero);

    private static Approval Pending() => new()
    {
        TenantId = Guid.NewGuid(),
        Module = "Request",
        RecordId = Guid.NewGuid(),
        Stage = 1,
        Rule = ApprovalRule.AnyOne,
        TargetKind = ApprovalTargetKind.User,
        ApproverUserId = Guid.NewGuid(),
        State = ApprovalState.Pending,
        RecordLabel = "REQ0000042 - Standard laptop"
    };

    [Fact]
    public void Approving_records_who_decided_and_when()
    {
        var approval = Pending();
        var actor = Guid.NewGuid();

        approval.Approve(actor, Now, "Budget confirmed.");

        approval.State.ShouldBe(ApprovalState.Approved);
        approval.DecidedByUserId.ShouldBe(actor);
        approval.DecidedAt.ShouldBe(Now);
        approval.Comment.ShouldBe("Budget confirmed.");
        approval.IsOutstanding.ShouldBeFalse();
    }

    [Fact]
    public void Approving_without_a_comment_is_allowed()
    {
        var approval = Pending();

        approval.Approve(Guid.NewGuid(), Now, null);

        approval.State.ShouldBe(ApprovalState.Approved);
        approval.Comment.ShouldBeNull();
    }

    [Fact]
    public void Rejecting_requires_a_reason()
    {
        // An unexplained rejection sends the requester straight to the service desk to ask why,
        // which costs more than requiring one sentence here.
        var approval = Pending();

        var error = Should.Throw<DomainException>(
            () => approval.Reject(Guid.NewGuid(), Now, "   "));

        error.Code.ShouldBe("approval.reason_required");
        approval.State.ShouldBe(ApprovalState.Pending);
    }

    [Fact]
    public void Rejecting_with_a_reason_records_it()
    {
        var approval = Pending();

        approval.Reject(Guid.NewGuid(), Now, "  Not in this year's budget.  ");

        approval.State.ShouldBe(ApprovalState.Rejected);
        approval.Comment.ShouldBe("Not in this year's budget.");
    }

    [Fact]
    public void A_settled_approval_cannot_be_decided_again()
    {
        var approval = Pending();
        approval.Approve(Guid.NewGuid(), Now, "Fine by me.");

        var error = Should.Throw<DomainException>(
            () => approval.Reject(Guid.NewGuid(), Now.AddHours(1), "Changed my mind."));

        error.Code.ShouldBe("approval.already_decided");

        // The original decision survives the attempt.
        approval.State.ShouldBe(ApprovalState.Approved);
        approval.Comment.ShouldBe("Fine by me.");
    }

    [Fact]
    public void Cancelling_leaves_a_decision_that_was_already_made_untouched()
    {
        var approval = Pending();
        approval.Approve(Guid.NewGuid(), Now, "Approved.");

        approval.CancelIfOutstanding(Now.AddHours(2));

        approval.State.ShouldBe(ApprovalState.Approved);
    }

    [Fact]
    public void Cancelling_withdraws_an_outstanding_approval()
    {
        var approval = Pending();

        approval.CancelIfOutstanding(Now);

        approval.State.ShouldBe(ApprovalState.Cancelled);
    }
}

/// <summary>
/// The arithmetic that turns several individual approvals into one stage outcome.
/// </summary>
public sealed class ApprovalStageEvaluationTests
{
    [Fact]
    public void Unanimous_needs_every_approver_before_it_clears()
    {
        ApprovalStateMachine.Evaluate(
                [ApprovalState.Approved, ApprovalState.Pending],
                ApprovalRule.Unanimous)
            .ShouldBe(ApprovalStateMachine.StageOutcome.Pending);

        ApprovalStateMachine.Evaluate(
                [ApprovalState.Approved, ApprovalState.Approved],
                ApprovalRule.Unanimous)
            .ShouldBe(ApprovalStateMachine.StageOutcome.Approved);
    }

    [Fact]
    public void AnyOne_clears_on_the_first_approval()
        => ApprovalStateMachine.Evaluate(
                [ApprovalState.Approved, ApprovalState.Pending, ApprovalState.Pending],
                ApprovalRule.AnyOne)
            .ShouldBe(ApprovalStateMachine.StageOutcome.Approved);

    [Theory]
    [InlineData(ApprovalRule.Unanimous)]
    [InlineData(ApprovalRule.AnyOne)]
    public void A_rejection_is_decisive_under_either_rule(ApprovalRule rule)
        => ApprovalStateMachine.Evaluate(
                [ApprovalState.Approved, ApprovalState.Rejected, ApprovalState.Pending],
                rule)
            .ShouldBe(ApprovalStateMachine.StageOutcome.Rejected);

    [Fact]
    public void Cancelled_and_superseded_approvals_are_ignored_in_the_count()
    {
        ApprovalStateMachine.Evaluate(
                [ApprovalState.Approved, ApprovalState.NotRequired, ApprovalState.Cancelled],
                ApprovalRule.Unanimous)
            .ShouldBe(ApprovalStateMachine.StageOutcome.Approved);
    }

    [Fact]
    public void A_stage_with_no_live_approvers_clears_rather_than_stranding_the_request()
    {
        // Reached when an item is configured to need approval but every candidate resolved to
        // nobody - a requester with no line manager, say. Failing closed would leave the request
        // permanently stuck with no one able to release it.
        ApprovalStateMachine.Evaluate([], ApprovalRule.Unanimous)
            .ShouldBe(ApprovalStateMachine.StageOutcome.Approved);

        ApprovalStateMachine.Evaluate(
                [ApprovalState.Cancelled, ApprovalState.NotRequired],
                ApprovalRule.AnyOne)
            .ShouldBe(ApprovalStateMachine.StageOutcome.Approved);
    }

    [Fact]
    public void A_stage_nobody_has_touched_is_still_pending()
        => ApprovalStateMachine.Evaluate(
                [ApprovalState.Pending, ApprovalState.Pending],
                ApprovalRule.AnyOne)
            .ShouldBe(ApprovalStateMachine.StageOutcome.Pending);
}
