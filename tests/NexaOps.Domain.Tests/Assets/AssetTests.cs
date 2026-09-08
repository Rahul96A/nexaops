using NexaOps.Domain.Assets;
using NexaOps.Domain.Common;

namespace NexaOps.Domain.Tests.Assets;

/// <summary>
/// Custody and lifecycle of a physical asset.
/// <para>
/// The rule that matters is that custody is history, not a field: "who had this laptop in March"
/// is what an audit or a security incident actually asks.
/// </para>
/// </summary>
public sealed class AssetTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private static Asset Laptop() => new()
    {
        TenantId = Guid.NewGuid(),
        Number = "AST0000042",
        AssetTag = "NX-1042",
        Name = "ThinkPad T14",
        Kind = AssetKind.Hardware,
        Status = AssetStatus.InStock
    };

    [Fact]
    public void Issuing_an_asset_opens_a_custody_record()
    {
        var asset = Laptop();
        var user = Guid.NewGuid();

        var assignment = asset.AssignTo(user, Now, "New starter kit.");

        asset.Status.ShouldBe(AssetStatus.Assigned);
        asset.AssignedToUserId.ShouldBe(user);
        assignment.IsOpen.ShouldBeTrue();
        assignment.AssignmentNote.ShouldBe("New starter kit.");
    }

    [Fact]
    public void Reassigning_closes_the_previous_custody_rather_than_overwriting_it()
    {
        // A single mutable holder field cannot answer "who had this in March".
        var asset = Laptop();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        asset.AssignTo(first, Now, null);
        asset.AssignTo(second, Now.AddDays(30), "Handed over on transfer.");

        asset.AssignmentHistory.Count.ShouldBe(2);
        asset.AssignmentHistory.Count(a => a.IsOpen).ShouldBe(1);

        var closed = asset.AssignmentHistory.Single(a => a.UserId == first);
        closed.ReturnedAt.ShouldBe(Now.AddDays(30));
        closed.HeldFor(Now.AddDays(60)).ShouldBe(TimeSpan.FromDays(30));

        asset.AssignedToUserId.ShouldBe(second);
    }

    [Fact]
    public void A_disposed_asset_cannot_be_issued_to_anyone()
    {
        var asset = Laptop();
        asset.Dispose(new DateOnly(2026, 9, 1), "Scrapped.", Now);

        var error = Should.Throw<DomainException>(() => asset.AssignTo(Guid.NewGuid(), Now, null));

        error.Code.ShouldBe("asset.not_in_service");
        error.Message.ShouldContain("Disposed");
    }

    [Fact]
    public void Returning_an_asset_nobody_holds_is_refused()
        => Should.Throw<DomainException>(() => Laptop().Return(Now, null))
            .Code.ShouldBe("asset.not_assigned");

    [Fact]
    public void Returning_closes_custody_and_puts_it_back_in_stock()
    {
        var asset = Laptop();
        asset.AssignTo(Guid.NewGuid(), Now, null);

        asset.Return(Now.AddDays(400), "Returned on leaving.");

        asset.Status.ShouldBe(AssetStatus.InStock);
        asset.AssignedToUserId.ShouldBeNull();
        asset.AssignmentHistory.ShouldAllBe(a => !a.IsOpen);
        asset.AssignmentHistory.Single().ReturnNote.ShouldBe("Returned on leaving.");
    }

    [Fact]
    public void A_faulty_asset_can_be_returned_straight_into_repair()
    {
        var asset = Laptop();
        asset.AssignTo(Guid.NewGuid(), Now, null);

        asset.Return(Now.AddDays(10), "Screen cracked.", AssetStatus.InRepair);

        asset.Status.ShouldBe(AssetStatus.InRepair);

        // Still in service: the organisation owns it and will use it again.
        asset.IsInService.ShouldBeTrue();
    }

    [Fact]
    public void Disposing_closes_custody_so_nobody_stays_accountable_for_a_thing_that_is_gone()
    {
        var asset = Laptop();
        var holder = Guid.NewGuid();
        asset.AssignTo(holder, Now, null);

        asset.Dispose(new DateOnly(2026, 12, 31), "Written off after water damage.", Now.AddDays(5));

        asset.Status.ShouldBe(AssetStatus.Disposed);
        asset.AssignedToUserId.ShouldBeNull();
        asset.AssignmentHistory.Single(a => a.UserId == holder).IsOpen.ShouldBeFalse();
        asset.IsInService.ShouldBeFalse();
    }

    [Fact]
    public void A_refresh_date_is_absent_rather_than_guessed()
    {
        // A date invented from a missing purchase date would be worse than none, because
        // somebody would budget against it.
        var asset = Laptop();
        asset.UsefulLifeMonths = 36;

        asset.RefreshDueOn.ShouldBeNull();

        asset.PurchasedOn = new DateOnly(2024, 4, 1);
        asset.RefreshDueOn.ShouldBe(new DateOnly(2027, 4, 1));
    }

    [Fact]
    public void A_zero_or_missing_useful_life_yields_no_refresh_date()
    {
        var asset = Laptop();
        asset.PurchasedOn = new DateOnly(2024, 4, 1);

        asset.RefreshDueOn.ShouldBeNull();

        asset.UsefulLifeMonths = 0;
        asset.RefreshDueOn.ShouldBeNull();
    }

    [Fact]
    public void Refresh_and_warranty_are_judged_against_a_supplied_date()
    {
        var asset = Laptop();
        asset.PurchasedOn = new DateOnly(2023, 1, 1);
        asset.UsefulLifeMonths = 36;
        asset.WarrantyExpiresOn = new DateOnly(2026, 1, 1);

        asset.IsDueForRefresh(new DateOnly(2025, 12, 31)).ShouldBeFalse();
        asset.IsDueForRefresh(new DateOnly(2026, 1, 1)).ShouldBeTrue();

        asset.IsOutOfWarranty(new DateOnly(2025, 12, 31)).ShouldBeFalse();
        asset.IsOutOfWarranty(new DateOnly(2026, 1, 2)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(AssetStatus.InStock, true)]
    [InlineData(AssetStatus.Assigned, true)]
    [InlineData(AssetStatus.InRepair, true)]
    [InlineData(AssetStatus.OnOrder, false)]
    [InlineData(AssetStatus.Retired, false)]
    [InlineData(AssetStatus.Disposed, false)]
    [InlineData(AssetStatus.Lost, false)]
    public void In_service_means_owned_and_usable(AssetStatus status, bool expected)
        => new Asset { Status = status }.IsInService.ShouldBe(expected);
}

/// <summary>
/// Licence compliance.
/// <para>
/// The position is computed from the numbers rather than stored as a field somebody remembers to
/// update — a register that can disagree with itself answers nothing.
/// </para>
/// </summary>
public sealed class SoftwareLicenceTests
{
    private static readonly DateOnly Today = new(2026, 9, 8);

    private static SoftwareLicence Licence(int entitlements, int deployed) => new()
    {
        TenantId = Guid.NewGuid(),
        Number = "LIC0000042",
        ProductName = "Design Suite",
        Model = LicenceModel.PerUser,
        EntitlementCount = entitlements,
        DeployedCount = deployed
    };

    [Fact]
    public void Over_deployment_is_reported_with_the_number_that_appears_on_an_audit_finding()
    {
        var licence = Licence(50, 63);

        licence.ComplianceAt(Today).ShouldBe(ComplianceState.OverDeployed);
        licence.OverDeployedBy.ShouldBe(13);
        licence.AvailableEntitlements.ShouldBe(-13);
    }

    [Fact]
    public void Comfortable_usage_is_compliant_rather_than_under_used()
    {
        // Flagging 80%+ as spare capacity would invite somebody to cancel seats they are about
        // to need.
        Licence(100, 80).ComplianceAt(Today).ShouldBe(ComplianceState.Compliant);
        Licence(100, 79).ComplianceAt(Today).ShouldBe(ComplianceState.UnderUsed);
    }

    [Fact]
    public void An_expired_agreement_beats_a_comfortable_seat_count()
    {
        // Every deployment against a lapsed agreement is unlicensed, however good the numbers
        // look.
        var licence = Licence(100, 10);
        licence.ExpiresOn = Today.AddDays(-1);

        licence.ComplianceAt(Today).ShouldBe(ComplianceState.Expired);
    }

    [Fact]
    public void An_agreement_expiring_today_is_still_in_force()
    {
        var licence = Licence(100, 10);
        licence.ExpiresOn = Today;

        licence.HasExpired(Today).ShouldBeFalse();
        licence.ComplianceAt(Today).ShouldNotBe(ComplianceState.Expired);
    }

    [Fact]
    public void A_perpetual_licence_never_expires()
    {
        var licence = Licence(10, 10);
        licence.ExpiresOn = null;

        licence.HasExpired(new DateOnly(2099, 1, 1)).ShouldBeFalse();
    }

    [Fact]
    public void A_site_licence_is_counted_for_cost_but_not_for_compliance()
    {
        var licence = Licence(0, 5000);
        licence.Model = LicenceModel.SiteLicence;

        licence.ComplianceAt(Today).ShouldBe(ComplianceState.Compliant);
        licence.OverDeployedBy.ShouldBe(0);
        licence.AvailableEntitlements.ShouldBeNull();
    }

    [Fact]
    public void A_site_licence_still_expires()
    {
        var licence = Licence(0, 5000);
        licence.Model = LicenceModel.SiteLicence;
        licence.ExpiresOn = Today.AddDays(-1);

        licence.ComplianceAt(Today).ShouldBe(ComplianceState.Expired);
    }

    [Fact]
    public void Counts_cannot_be_negative()
    {
        var licence = Licence(10, 5);

        Should.Throw<DomainException>(() => licence.SetDeployedCount(-1))
            .Code.ShouldBe("licence.negative_deployment");

        Should.Throw<DomainException>(() => licence.SetEntitlementCount(-1))
            .Code.ShouldBe("licence.negative_entitlement");
    }

    [Fact]
    public void A_licence_with_no_entitlements_and_no_deployments_is_not_over_deployed()
    {
        var licence = Licence(0, 0);

        licence.OverDeployedBy.ShouldBe(0);
        licence.ComplianceAt(Today).ShouldBe(ComplianceState.UnderUsed);
    }
}
