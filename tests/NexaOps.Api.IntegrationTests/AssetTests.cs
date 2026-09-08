using System.Net;
using System.Net.Http.Json;
using NexaOps.Api.IntegrationTests.Infrastructure;
using NexaOps.Application.Assets;
using NexaOps.Application.Common;
using NexaOps.Domain.Assets;

namespace NexaOps.Api.IntegrationTests;

/// <summary>
/// The asset register end to end, over real HTTP against real SQL Server.
/// <para>
/// The behaviours worth protecting are that custody is history rather than a field, that disposal
/// closes it so nobody stays accountable for a thing that is gone, and that licence compliance is
/// computed rather than asserted.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AssetTests
{
    private readonly TestEnvironment _env;

    public AssetTests(TestEnvironment env) => _env = env;

    [Fact]
    public async Task Two_assets_cannot_share_a_tag()
    {
        // The tag is what a finance audit physically counts, so a duplicate makes the register
        // unreconcilable against the floor.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var tag = $"NX-{Guid.NewGuid():N}"[..12];
        await CreateAsync(manager, tag);

        var response = await manager.PostAsJsonAsync("/api/v1/assets",
            new UpsertAssetCommand { AssetTag = tag, Name = "Duplicate" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Warranty_cannot_expire_before_the_asset_was_purchased()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var response = await manager.PostAsJsonAsync("/api/v1/assets", new UpsertAssetCommand
        {
            AssetTag = $"BW-{Guid.NewGuid():N}"[..12],
            Name = "Backwards warranty",
            PurchasedOn = new DateOnly(2026, 6, 1),
            WarrantyExpiresOn = new DateOnly(2025, 6, 1)
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Custody_is_history_rather_than_a_single_holder_field()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var asset = await CreateAsync(manager, $"HX-{Guid.NewGuid():N}"[..12]);

        await AssignAsync(manager, asset.Id, _env.Acme.Agent.Id, "First issue.");
        var reassigned = await AssignAsync(manager, asset.Id, _env.Acme.Employee.Id, "Handed over.");

        // "Who had this in March" has to be answerable.
        reassigned.CustodyHistory.Count.ShouldBe(2);
        reassigned.CustodyHistory.Count(h => h.ReturnedAt is null).ShouldBe(1);
        reassigned.AssignedToUserId.ShouldBe(_env.Acme.Employee.Id);

        var closed = reassigned.CustodyHistory.Single(h => h.UserId == _env.Acme.Agent.Id);
        closed.ReturnedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Returning_closes_custody_and_puts_the_asset_back_in_stock()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var asset = await CreateAsync(manager, $"RT-{Guid.NewGuid():N}"[..12]);
        await AssignAsync(manager, asset.Id, _env.Acme.Agent.Id, null);

        var response = await manager.PostAsJsonAsync($"/api/v1/assets/{asset.Id}/return",
            new ReturnAssetCommand { Note = "Returned on leaving." });

        response.EnsureSuccessStatusCode();

        var returned = await response.Content.ReadFromJsonAsync<AssetDetailDto>(TestEnvironment.Json);
        returned!.Status.ShouldBe(AssetStatus.InStock);
        returned.AssignedToUserId.ShouldBeNull();
        returned.CustodyHistory.ShouldAllBe(h => h.ReturnedAt != null);
    }

    [Fact]
    public async Task A_faulty_asset_can_be_returned_straight_into_repair()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var asset = await CreateAsync(manager, $"FX-{Guid.NewGuid():N}"[..12]);
        await AssignAsync(manager, asset.Id, _env.Acme.Agent.Id, null);

        var response = await manager.PostAsJsonAsync($"/api/v1/assets/{asset.Id}/return",
            new ReturnAssetCommand { Note = "Screen cracked.", ReturnTo = AssetStatus.InRepair });

        response.EnsureSuccessStatusCode();

        var returned = await response.Content.ReadFromJsonAsync<AssetDetailDto>(TestEnvironment.Json);
        returned!.Status.ShouldBe(AssetStatus.InRepair);
    }

    [Fact]
    public async Task Returning_an_asset_nobody_holds_is_refused()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);
        var asset = await CreateAsync(manager, $"NH-{Guid.NewGuid():N}"[..12]);

        var response = await manager.PostAsJsonAsync($"/api/v1/assets/{asset.Id}/return",
            new ReturnAssetCommand());

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("asset.not_assigned");
    }

    [Fact]
    public async Task A_service_desk_manager_can_issue_kit_but_not_dispose_of_it()
    {
        // Disposal removes something a finance audit expects to be able to count, so it stays
        // with the asset manager.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var asset = await CreateAsync(manager, $"DP-{Guid.NewGuid():N}"[..12]);

        var response = await manager.PostAsJsonAsync($"/api/v1/assets/{asset.Id}/dispose",
            new DisposeAssetCommand { DisposedOn = DateOnly.FromDateTime(DateTime.UtcNow) });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Disposal_cannot_be_reached_through_a_general_edit()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var asset = await CreateAsync(manager, $"BP-{Guid.NewGuid():N}"[..12]);

        var response = await manager.PutAsJsonAsync($"/api/v1/assets/{asset.Id}", new UpsertAssetCommand
        {
            AssetTag = asset.AssetTag,
            Name = asset.Name,
            Status = AssetStatus.Disposed
        });

        // Otherwise custody would stay open and no disposal date would be recorded.
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("asset.use_disposal_endpoint");
    }

    [Fact]
    public async Task A_refresh_date_is_absent_rather_than_guessed()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var withoutPurchase = await manager.PostAsJsonAsync("/api/v1/assets", new UpsertAssetCommand
        {
            AssetTag = $"NR-{Guid.NewGuid():N}"[..12],
            Name = "No purchase date",
            UsefulLifeMonths = 36
        });

        withoutPurchase.EnsureSuccessStatusCode();

        var asset = await withoutPurchase.Content.ReadFromJsonAsync<AssetDetailDto>(TestEnvironment.Json);

        // A date invented from a missing purchase date would be worse than none, because
        // somebody would budget against it.
        asset!.RefreshDueOn.ShouldBeNull();
        asset.IsDueForRefresh.ShouldBeFalse();
    }

    [Fact]
    public async Task An_asset_past_its_useful_life_is_flagged_for_refresh()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var response = await manager.PostAsJsonAsync("/api/v1/assets", new UpsertAssetCommand
        {
            AssetTag = $"OL-{Guid.NewGuid():N}"[..12],
            Name = "Well past its day",
            PurchasedOn = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-5),
            UsefulLifeMonths = 36
        });

        response.EnsureSuccessStatusCode();

        var asset = await response.Content.ReadFromJsonAsync<AssetDetailDto>(TestEnvironment.Json);
        asset!.RefreshDueOn.ShouldNotBeNull();
        asset.IsDueForRefresh.ShouldBeTrue();
    }

    [Fact]
    public async Task Licence_compliance_is_computed_from_the_numbers()
    {
        var assetManager = await _env.ClientForAsync(_env.Acme.Manager);

        var created = await assetManager.PostAsJsonAsync("/api/v1/assets/licences", new UpsertLicenceCommand
        {
            ProductName = $"Design Suite {Guid.NewGuid():N}"[..20],
            Model = LicenceModel.PerUser,
            EntitlementCount = 50,
            DeployedCount = 63,
            AnnualCost = 500000m
        });

        // The service desk manager holds licence.read but not licence.manage.
        created.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_expired_agreement_beats_a_comfortable_seat_count()
    {
        var admin = await _env.ClientForAsync(_env.Acme.AssetManager);

        var response = await admin.PostAsJsonAsync("/api/v1/assets/licences", new UpsertLicenceCommand
        {
            ProductName = $"Lapsed {Guid.NewGuid():N}"[..20],
            Model = LicenceModel.PerUser,
            EntitlementCount = 100,
            DeployedCount = 10,
            ExpiresOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)
        });

        response.EnsureSuccessStatusCode();

        var licence = await response.Content.ReadFromJsonAsync<LicenceSummaryDto>(TestEnvironment.Json);

        // Every deployment against a lapsed agreement is unlicensed, however good the numbers.
        licence!.Compliance.ShouldBe(ComplianceState.Expired);
    }

    [Fact]
    public async Task Over_deployment_is_reported_with_the_number_that_appears_on_an_audit_finding()
    {
        var admin = await _env.ClientForAsync(_env.Acme.AssetManager);

        var response = await admin.PostAsJsonAsync("/api/v1/assets/licences", new UpsertLicenceCommand
        {
            ProductName = $"Over {Guid.NewGuid():N}"[..20],
            Model = LicenceModel.PerUser,
            EntitlementCount = 50,
            DeployedCount = 63
        });

        response.EnsureSuccessStatusCode();

        var licence = await response.Content.ReadFromJsonAsync<LicenceSummaryDto>(TestEnvironment.Json);
        licence!.Compliance.ShouldBe(ComplianceState.OverDeployed);
        licence.OverDeployedBy.ShouldBe(13);
        licence.AvailableEntitlements.ShouldBe(-13);
    }

    [Fact]
    public async Task A_licence_cannot_expire_before_it_starts()
    {
        var admin = await _env.ClientForAsync(_env.Acme.AssetManager);

        var response = await admin.PostAsJsonAsync("/api/v1/assets/licences", new UpsertLicenceCommand
        {
            ProductName = "Backwards dates",
            StartsOn = new DateOnly(2026, 6, 1),
            ExpiresOn = new DateOnly(2025, 6, 1)
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Another_tenants_asset_is_not_reachable()
    {
        var acme = await _env.ClientForAsync(_env.Acme.Manager);
        var northwind = await _env.ClientForAsync(_env.Northwind.Manager);

        var asset = await CreateAsync(acme, $"AC-{Guid.NewGuid():N}"[..12]);

        (await northwind.GetAsync($"/api/v1/assets/{asset.Id}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var search = await northwind.GetFromJsonAsync<PagedResult<AssetSummaryDto>>(
            $"/api/v1/assets?search={asset.AssetTag}", TestEnvironment.Json);

        search!.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task An_asset_cannot_be_issued_to_somebody_in_another_tenants_directory()
    {
        var acme = await _env.ClientForAsync(_env.Acme.Manager);

        var asset = await CreateAsync(acme, $"XT-{Guid.NewGuid():N}"[..12]);

        var response = await acme.PostAsJsonAsync($"/api/v1/assets/{asset.Id}/assign",
            new AssignAssetCommand { UserId = _env.Northwind.Agent.Id });

        // Reads as a non-existent user, because the look-up is tenant-filtered.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unrecognised_sort_field_is_a_validation_failure()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var response = await manager.GetAsync("/api/v1/assets?sortBy=; DROP TABLE Assets");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static async Task<AssetDetailDto> CreateAsync(HttpClient client, string tag)
    {
        var response = await client.PostAsJsonAsync("/api/v1/assets", new UpsertAssetCommand
        {
            AssetTag = tag,
            Name = "ThinkPad T14",
            Kind = AssetKind.Hardware,
            Manufacturer = "Lenovo",
            PurchaseCost = 74500m
        });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AssetDetailDto>(TestEnvironment.Json))!;
    }

    private static async Task<AssetDetailDto> AssignAsync(
        HttpClient client,
        Guid assetId,
        Guid userId,
        string? note)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/assets/{assetId}/assign",
            new AssignAssetCommand { UserId = userId, Note = note });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AssetDetailDto>(TestEnvironment.Json))!;
    }
}
