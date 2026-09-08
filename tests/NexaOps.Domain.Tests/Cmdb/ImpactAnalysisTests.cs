using NexaOps.Domain.Cmdb;
using NexaOps.Domain.Common;

namespace NexaOps.Domain.Tests.Cmdb;

/// <summary>
/// Walking the dependency graph.
/// <para>
/// The rules that matter operationally: direction is not symmetric, the shallowest path wins so
/// urgency is not understated, and a cyclic graph terminates — because a real CMDB contains
/// cycles and they are correct ones.
/// </para>
/// </summary>
public sealed class ImpactAnalysisTests
{
    // A small but realistic estate:
    //   Payroll service  depends on  Payroll app
    //   Payroll app      depends on  Database
    //   Reporting app    depends on  Database
    //   Database         depends on  Server
    private static readonly Guid Server = Guid.NewGuid();
    private static readonly Guid Database = Guid.NewGuid();
    private static readonly Guid PayrollApp = Guid.NewGuid();
    private static readonly Guid ReportingApp = Guid.NewGuid();
    private static readonly Guid PayrollService = Guid.NewGuid();

    private static List<CiRelationship> Estate() =>
    [
        Edge(PayrollService, PayrollApp),
        Edge(PayrollApp, Database),
        Edge(ReportingApp, Database),
        Edge(Database, Server, CiRelationshipType.RunsOn)
    ];

    private static CiRelationship Edge(
        Guid source,
        Guid target,
        CiRelationshipType type = CiRelationshipType.DependsOn) => new()
    {
        TenantId = Guid.NewGuid(),
        SourceId = source,
        TargetId = target,
        Type = type
    };

    [Fact]
    public void Breaking_the_server_affects_everything_above_it()
    {
        var impacted = ImpactAnalysis.WhatBreaksIf(Server, Estate());

        impacted.Select(i => i.ItemId)
            .ShouldBe([Database, PayrollApp, ReportingApp, PayrollService], ignoreOrder: true);
    }

    [Fact]
    public void Depth_reflects_how_many_hops_away_the_effect_is()
    {
        var impacted = ImpactAnalysis.WhatBreaksIf(Server, Estate());

        impacted.Single(i => i.ItemId == Database).Depth.ShouldBe(1);
        impacted.Single(i => i.ItemId == PayrollApp).Depth.ShouldBe(2);
        impacted.Single(i => i.ItemId == PayrollService).Depth.ShouldBe(3);
    }

    [Fact]
    public void Direction_is_not_symmetric()
    {
        // Breaking the payroll service affects nothing else; breaking what it stands on affects
        // it. Treating the edge as undirected would make impact analysis useless.
        ImpactAnalysis.WhatBreaksIf(PayrollService, Estate()).ShouldBeEmpty();

        ImpactAnalysis.WhatItDependsOn(PayrollService, Estate())
            .Select(i => i.ItemId)
            .ShouldBe([PayrollApp, Database, Server], ignoreOrder: true);
    }

    [Fact]
    public void The_shallowest_path_to_an_item_wins()
    {
        // Reachable at depth 1 directly and at depth 2 through the database. Reporting it as two
        // hops away would understate the urgency.
        var edges = Estate();
        edges.Add(Edge(PayrollApp, Server));

        var impacted = ImpactAnalysis.WhatBreaksIf(Server, edges);

        impacted.Single(i => i.ItemId == PayrollApp).Depth.ShouldBe(1);
    }

    [Fact]
    public void A_cycle_terminates_rather_than_looping()
    {
        // Two servers that each fail over to the other is a cycle, and a correct one.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var edges = new List<CiRelationship>
        {
            Edge(a, b, CiRelationshipType.FailsOverTo),
            Edge(b, a, CiRelationshipType.FailsOverTo)
        };

        var impacted = ImpactAnalysis.WhatBreaksIf(a, edges);

        impacted.ShouldHaveSingleItem().ItemId.ShouldBe(b);
    }

    [Fact]
    public void An_item_is_never_reported_as_impacting_itself()
        => ImpactAnalysis.WhatBreaksIf(Server, Estate())
            .ShouldNotContain(i => i.ItemId == Server);

    [Fact]
    public void The_walk_stops_at_the_requested_depth()
    {
        var impacted = ImpactAnalysis.WhatBreaksIf(Server, Estate(), maxDepth: 2);

        impacted.Select(i => i.ItemId).ShouldBe([Database, PayrollApp, ReportingApp], ignoreOrder: true);
        impacted.ShouldNotContain(i => i.ItemId == PayrollService);
    }

    [Fact]
    public void Depth_is_clamped_so_a_long_chain_cannot_turn_a_page_into_a_timeout()
    {
        var edges = new List<CiRelationship>();
        var previous = Guid.NewGuid();
        var root = previous;

        // A chain far longer than anything actionable.
        for (var i = 0; i < 50; i++)
        {
            var next = Guid.NewGuid();
            edges.Add(Edge(next, previous));
            previous = next;
        }

        var impacted = ImpactAnalysis.WhatBreaksIf(root, edges, maxDepth: 1000);

        impacted.Count.ShouldBe(ImpactAnalysis.MaxDepth);
        impacted.Max(i => i.Depth).ShouldBe(ImpactAnalysis.MaxDepth);
    }

    [Fact]
    public void An_isolated_item_impacts_nothing()
        => ImpactAnalysis.WhatBreaksIf(Guid.NewGuid(), Estate()).ShouldBeEmpty();

    [Fact]
    public void The_relationship_that_led_to_each_item_is_reported()
    {
        var impacted = ImpactAnalysis.WhatBreaksIf(Server, Estate());

        impacted.Single(i => i.ItemId == Database).ViaRelationship
            .ShouldBe(CiRelationshipType.RunsOn);
    }
}

/// <summary>The few invariants a configuration item genuinely has.</summary>
public sealed class ConfigurationItemTests
{
    [Fact]
    public void An_item_cannot_be_related_to_itself()
    {
        var id = Guid.NewGuid();
        var edge = new CiRelationship { SourceId = id, TargetId = id };

        Should.Throw<DomainException>(edge.EnsureValid).Code.ShouldBe("cmdb.self_relationship");
    }

    [Fact]
    public void Out_of_support_is_judged_against_a_supplied_date()
    {
        // An expired warranty discovered during an outage is the most expensive way to learn
        // about it, so this is a first-class question rather than a report.
        var item = new ConfigurationItem
        {
            Name = "sql-prod-01",
            SupportExpiresOn = new DateOnly(2026, 3, 31)
        };

        item.IsOutOfSupport(new DateOnly(2026, 3, 30)).ShouldBeFalse();
        item.IsOutOfSupport(new DateOnly(2026, 4, 1)).ShouldBeTrue();
    }

    [Fact]
    public void An_item_with_no_support_date_is_never_out_of_support()
        => new ConfigurationItem().IsOutOfSupport(new DateOnly(2030, 1, 1)).ShouldBeFalse();

    [Theory]
    [InlineData(CiStatus.Operational, true)]
    [InlineData(CiStatus.Impaired, true)]
    [InlineData(CiStatus.Planned, false)]
    [InlineData(CiStatus.Retired, false)]
    [InlineData(CiStatus.Disposed, false)]
    public void Live_means_in_service_and_expected_to_work(CiStatus status, bool expected)
        => new ConfigurationItem { Status = status }.IsLive.ShouldBe(expected);

    [Fact]
    public void A_disposed_item_can_come_back()
    {
        // A CMDB reflects reality rather than governing it. A server found in a cupboard really
        // can return to service, and refusing that only teaches people to keep a spreadsheet.
        var item = new ConfigurationItem { Status = CiStatus.Disposed };

        item.ChangeStatus(CiStatus.Operational);

        item.Status.ShouldBe(CiStatus.Operational);
    }
}
