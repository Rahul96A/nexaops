using System.Text.Json;
using NexaOps.Domain.Approvals;
using NexaOps.Domain.Catalog;
using NexaOps.Domain.Common;

namespace NexaOps.Domain.Tests.Catalog;

/// <summary>
/// What a catalogue item will and will not allow, and how it validates the answers it asks for.
/// </summary>
public sealed class CatalogItemTests
{
    private static CatalogItem Item() => new()
    {
        TenantId = Guid.NewGuid(),
        Code = "LAPTOP-STD",
        Name = "Standard laptop",
        FulfilmentGroupId = Guid.NewGuid(),
        Status = CatalogItemStatus.Draft
    };

    [Fact]
    public void An_item_without_a_fulfilment_group_cannot_be_published()
    {
        // Publishing it would put orders into a queue nobody owns, which the requester
        // experiences as silence.
        var item = Item();
        item.FulfilmentGroupId = null;

        Should.Throw<DomainException>(item.Publish)
            .Code.ShouldBe("catalog.no_fulfilment_group");

        item.Status.ShouldBe(CatalogItemStatus.Draft);
    }

    [Fact]
    public void An_item_needing_a_named_approver_cannot_be_published_without_one()
    {
        var item = Item();
        item.RequiresApproval = true;
        item.ApprovalTargetKind = ApprovalTargetKind.User;
        item.ApproverUserId = null;

        Should.Throw<DomainException>(item.Publish).Code.ShouldBe("catalog.no_approver");
    }

    [Fact]
    public void An_item_needing_an_approving_group_cannot_be_published_without_one()
    {
        var item = Item();
        item.RequiresApproval = true;
        item.ApprovalTargetKind = ApprovalTargetKind.Group;
        item.ApproverGroupId = null;

        Should.Throw<DomainException>(item.Publish).Code.ShouldBe("catalog.no_approver");
    }

    [Fact]
    public void Manager_approval_needs_no_configured_approver_because_it_resolves_at_order_time()
    {
        var item = Item();
        item.RequiresApproval = true;
        item.ApprovalTargetKind = ApprovalTargetKind.Manager;

        item.Publish();

        item.Status.ShouldBe(CatalogItemStatus.Published);
    }

    [Fact]
    public void Only_a_published_item_is_orderable()
    {
        var item = Item();
        item.IsOrderable.ShouldBeFalse();

        item.Publish();
        item.IsOrderable.ShouldBeTrue();

        item.Retire();
        item.IsOrderable.ShouldBeFalse();
    }

    [Fact]
    public void An_archived_item_is_not_orderable_even_when_published()
    {
        var item = Item();
        item.Publish();
        item.IsArchived = true;

        item.IsOrderable.ShouldBeFalse();
    }
}

/// <summary>
/// Server-side validation of catalogue answers. The React form is a convenience for the person
/// filling it in; these rules are the control.
/// </summary>
public sealed class CatalogItemVariableTests
{
    private static CatalogItemVariable Variable(VariableType type, bool required = false) => new()
    {
        TenantId = Guid.NewGuid(),
        Key = "field",
        Label = "Cost centre",
        Type = type,
        IsRequired = required
    };

    [Fact]
    public void A_required_field_left_blank_is_reported_by_its_label()
    {
        var variable = Variable(VariableType.Text, required: true);

        variable.Validate("   ").ShouldBe("Cost centre is required.");
        variable.Validate(null).ShouldBe("Cost centre is required.");
    }

    [Fact]
    public void An_optional_field_left_blank_is_accepted()
        => Variable(VariableType.Text).Validate(null).ShouldBeNull();

    [Fact]
    public void Text_longer_than_the_configured_maximum_is_refused()
    {
        var variable = Variable(VariableType.Text);
        variable.MaxLength = 5;

        variable.Validate("123456").ShouldBe("Cost centre must be 5 characters or fewer.");
        variable.Validate("12345").ShouldBeNull();
    }

    [Fact]
    public void A_number_field_refuses_text_and_enforces_its_bounds()
    {
        var variable = Variable(VariableType.Number);
        variable.MinValue = 1;
        variable.MaxValue = 10;

        variable.Validate("abc").ShouldBe("Cost centre must be a number.");
        variable.Validate("0").ShouldBe("Cost centre must be at least 1.");
        variable.Validate("11").ShouldBe("Cost centre must be at most 10.");
        variable.Validate("5").ShouldBeNull();
    }

    [Fact]
    public void A_date_field_requires_a_parseable_date()
    {
        var variable = Variable(VariableType.Date);

        variable.Validate("not a date").ShouldNotBeNull();
        variable.Validate("2026-09-08").ShouldBeNull();
    }

    [Fact]
    public void A_boolean_field_requires_true_or_false()
    {
        var variable = Variable(VariableType.Boolean);

        variable.Validate("yes").ShouldNotBeNull();
        variable.Validate("true").ShouldBeNull();
    }

    [Fact]
    public void A_choice_outside_the_offered_options_is_refused()
    {
        var variable = Variable(VariableType.Choice);
        variable.ChoicesJson = JsonSerializer.Serialize(new[] { "Windows", "macOS" });

        variable.Validate("Linux").ShouldBe("Cost centre must be one of the offered options.");
        variable.Validate("macOS").ShouldBeNull();
    }

    [Fact]
    public void A_choice_field_with_no_configured_options_refuses_every_answer()
    {
        // A misconfigured field must not silently accept anything, which would let a requester
        // put arbitrary text into what was meant to be a constrained selection.
        var variable = Variable(VariableType.Choice);

        variable.Validate("anything").ShouldBe(
            "Cost centre has no configured options, so it cannot be answered.");
    }

    [Fact]
    public void A_malformed_choice_list_does_not_throw_out_of_the_getter()
    {
        var variable = Variable(VariableType.Choice);
        variable.ChoicesJson = "{ this is not json";

        variable.Choices().ShouldBeEmpty();
        Should.NotThrow(() => variable.Validate("Windows"));
    }

    [Fact]
    public void A_multi_choice_answer_must_be_a_list_drawn_from_the_offered_options()
    {
        var variable = Variable(VariableType.MultiChoice);
        variable.ChoicesJson = JsonSerializer.Serialize(new[] { "Email", "VPN", "CRM" });

        variable.Validate("VPN").ShouldNotBeNull();
        variable.Validate(JsonSerializer.Serialize(new[] { "VPN", "Payroll" })).ShouldNotBeNull();
        variable.Validate(JsonSerializer.Serialize(new[] { "VPN", "CRM" })).ShouldBeNull();
    }

    [Fact]
    public void A_user_or_group_field_must_carry_an_identifier()
    {
        var user = Variable(VariableType.User);

        user.Validate("kavya.nair").ShouldBe("Cost centre must be a valid selection.");
        user.Validate(Guid.NewGuid().ToString()).ShouldBeNull();
    }
}
