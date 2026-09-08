using NexaOps.Domain.Common;
using NexaOps.Domain.Requests;

namespace NexaOps.Domain.Tests.Requests;

/// <summary>
/// The invariants the service request aggregate holds no matter which caller drives it.
/// </summary>
public sealed class ServiceRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 5, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.NewGuid();

    private static ServiceRequest Draft(int items = 1)
    {
        var request = new ServiceRequest
        {
            TenantId = Guid.NewGuid(),
            Number = "REQ0000042",
            Title = "Standard laptop for a new starter",
            RequesterId = Guid.NewGuid(),
            RequestedForId = Guid.NewGuid(),
            Status = RequestStatus.Draft
        };

        for (var i = 0; i < items; i++)
        {
            request.Items.Add(new RequestItem
            {
                TenantId = request.TenantId,
                CatalogItemId = Guid.NewGuid(),
                CatalogItemName = $"Item {i + 1}",
                Quantity = 1,
                UnitCost = 45000m,
                Status = RequestItemStatus.Pending
            });
        }

        return request;
    }

    [Fact]
    public void Submitting_an_item_that_needs_authorisation_routes_it_to_approval()
    {
        var request = Draft();

        request.Submit(requiresApproval: true, Actor, Now);

        request.Status.ShouldBe(RequestStatus.AwaitingApproval);
        request.SubmittedAt.ShouldBe(Now);
        request.ApprovedAt.ShouldBeNull();
    }

    [Fact]
    public void Submitting_an_item_that_needs_no_authorisation_goes_straight_to_fulfilment()
    {
        var request = Draft();

        request.Submit(requiresApproval: false, Actor, Now);

        request.Status.ShouldBe(RequestStatus.Approved);
        request.SubmittedAt.ShouldBe(Now);
        request.ApprovedAt.ShouldBe(Now);
    }

    [Fact]
    public void An_empty_request_cannot_be_submitted()
    {
        var request = Draft(items: 0);

        var error = Should.Throw<DomainException>(
            () => request.Submit(requiresApproval: false, Actor, Now));

        error.Code.ShouldBe("request.no_items");
        request.Status.ShouldBe(RequestStatus.Draft);
    }

    [Fact]
    public void A_request_can_only_be_submitted_once()
    {
        var request = Draft();
        request.Submit(requiresApproval: false, Actor, Now);

        var error = Should.Throw<DomainException>(
            () => request.Submit(requiresApproval: false, Actor, Now.AddMinutes(5)));

        error.Code.ShouldBe("request.not_draft");
    }

    [Fact]
    public void Rejecting_requires_a_reason_and_ends_the_request()
    {
        var request = Draft();
        request.Submit(requiresApproval: true, Actor, Now);

        Should.Throw<DomainException>(() => request.RecordRejected("  ", Actor, Now))
            .Code.ShouldBe("request.rejection_reason_required");

        request.RecordRejected("Not budgeted this quarter.", Actor, Now.AddHours(3));

        request.Status.ShouldBe(RequestStatus.Rejected);
        request.RejectionReason.ShouldBe("Not budgeted this quarter.");
        request.ClosedAt.ShouldBe(Now.AddHours(3));
    }

    [Fact]
    public void Putting_a_request_on_hold_records_why()
    {
        var request = Draft();
        request.Submit(requiresApproval: false, Actor, Now);
        request.TransitionTo(RequestStatus.InProgress, Actor, Now.AddMinutes(10));

        request.PutOnHold(RequestPendingReason.AwaitingStock, Actor, Now.AddMinutes(20));

        request.Status.ShouldBe(RequestStatus.Pending);
        request.PendingReason.ShouldBe(RequestPendingReason.AwaitingStock);
    }

    [Fact]
    public void Resuming_work_clears_a_stale_hold_reason()
    {
        var request = Draft();
        request.Submit(requiresApproval: false, Actor, Now);
        request.TransitionTo(RequestStatus.InProgress, Actor, Now.AddMinutes(10));
        request.PutOnHold(RequestPendingReason.AwaitingVendor, Actor, Now.AddMinutes(20));

        request.TransitionTo(RequestStatus.InProgress, Actor, Now.AddMinutes(30));

        request.PendingReason.ShouldBeNull();
    }

    [Fact]
    public void Cancelling_a_request_cancels_the_lines_that_had_not_been_delivered()
    {
        var request = Draft(items: 3);
        request.Submit(requiresApproval: false, Actor, Now);

        var delivered = request.Items.First();
        delivered.Fulfil(Actor, Now.AddHours(1), "Asset NX-1042");

        request.Cancel("Starter did not join.", Actor, Now.AddHours(2));

        request.Status.ShouldBe(RequestStatus.Cancelled);
        request.CancellationReason.ShouldBe("Starter did not join.");

        // What was already delivered stays delivered. Rewriting it would misreport what the
        // business actually spent.
        delivered.Status.ShouldBe(RequestItemStatus.Fulfilled);
        request.Items.Count(i => i.Status == RequestItemStatus.Cancelled).ShouldBe(2);
    }

    [Fact]
    public void A_request_is_only_complete_once_every_line_has_settled()
    {
        var request = Draft(items: 2);
        request.Submit(requiresApproval: false, Actor, Now);

        request.AllItemsSettled().ShouldBeFalse();

        request.Items.First().Fulfil(Actor, Now.AddHours(1), null);
        request.AllItemsSettled().ShouldBeFalse();

        request.Items.Last().Fulfil(Actor, Now.AddHours(2), null);
        request.AllItemsSettled().ShouldBeTrue();
    }

    [Fact]
    public void A_request_with_no_lines_is_never_reported_as_complete()
        => Draft(items: 0).AllItemsSettled().ShouldBeFalse();

    [Fact]
    public void A_line_cannot_be_delivered_twice()
    {
        var request = Draft();
        var item = request.Items.First();
        item.Fulfil(Actor, Now, "Asset NX-1042");

        Should.Throw<DomainException>(() => item.Fulfil(Actor, Now.AddHours(1), "Asset NX-9999"))
            .Code.ShouldBe("request.item_already_settled");

        item.FulfilmentNotes.ShouldBe("Asset NX-1042");
    }

    [Fact]
    public void Fulfilment_records_who_delivered_it_and_when()
    {
        var request = Draft();
        request.Submit(requiresApproval: false, Actor, Now);
        request.TransitionTo(RequestStatus.InProgress, Actor, Now.AddMinutes(5));

        request.TransitionTo(RequestStatus.Fulfilled, Actor, Now.AddHours(6));

        request.FulfilledAt.ShouldBe(Now.AddHours(6));
        request.FulfilledByUserId.ShouldBe(Actor);
    }

    [Fact]
    public void A_line_total_uses_the_price_captured_when_it_was_ordered()
    {
        var item = new RequestItem { UnitCost = 45000m, Quantity = 3 };

        item.LineCost.ShouldBe(135000m);
    }

    [Fact]
    public void A_line_with_no_price_has_no_total_rather_than_a_zero()
    {
        // Zero would read as "free" on an approval screen. Absent is the truth.
        new RequestItem { UnitCost = null, Quantity = 2 }.LineCost.ShouldBeNull();
    }
}
