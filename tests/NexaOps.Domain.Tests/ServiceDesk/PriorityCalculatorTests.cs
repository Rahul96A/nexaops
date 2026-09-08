using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Domain.Tests.ServiceDesk;

/// <summary>
/// Priority derivation from impact and urgency.
/// <para>
/// The tenant matrix always wins; the built-in mapping is only the fallback for a tenant whose
/// matrix has not finished seeding. Confusing the two would let a product default silently
/// override a customer's configuration, which is why both paths are pinned here.
/// </para>
/// </summary>
public sealed class PriorityCalculatorTests
{
    [Theory]
    [InlineData(Impact.Extensive, Urgency.Critical, Priority.P1Critical)]
    [InlineData(Impact.Extensive, Urgency.High, Priority.P2High)]
    [InlineData(Impact.Significant, Urgency.Critical, Priority.P2High)]
    [InlineData(Impact.Significant, Urgency.High, Priority.P2High)]
    [InlineData(Impact.Moderate, Urgency.Medium, Priority.P3Moderate)]
    [InlineData(Impact.Minor, Urgency.Medium, Priority.P4Low)]
    [InlineData(Impact.Minor, Urgency.Low, Priority.P5Planning)]
    public void The_default_matrix_maps_impact_and_urgency_to_priority(
        Impact impact,
        Urgency urgency,
        Priority expected)
        => PriorityCalculator.DefaultFor(impact, urgency).ShouldBe(expected);

    [Fact]
    public void The_default_matrix_covers_every_combination()
    {
        var matrix = PriorityCalculator.DefaultMatrix().ToList();

        matrix.Count.ShouldBe(
            Enum.GetValues<Impact>().Length * Enum.GetValues<Urgency>().Length);

        matrix.Select(m => (m.Impact, m.Urgency)).Distinct().Count().ShouldBe(matrix.Count);
    }

    [Fact]
    public void Severity_never_decreases_as_impact_worsens()
    {
        foreach (var urgency in Enum.GetValues<Urgency>())
        {
            // Impact is ranked 1 (worst) to 4 (least severe), as is priority, so a worse impact
            // must never produce a numerically larger - that is, less severe - priority.
            var priorities = Enum.GetValues<Impact>()
                .OrderBy(i => (int)i)
                .Select(i => (int)PriorityCalculator.DefaultFor(i, urgency))
                .ToList();

            priorities.ShouldBe(priorities.OrderBy(p => p).ToList());
        }
    }

    [Fact]
    public void A_tenant_matrix_entry_overrides_the_default()
    {
        var tenantId = Guid.NewGuid();

        // This customer treats a minor, low-urgency issue as critical - unusual, but it is
        // their configuration and the product must honour it.
        var matrix = new List<PriorityMatrixEntry>
        {
            new()
            {
                TenantId = tenantId,
                Impact = Impact.Minor,
                Urgency = Urgency.Low,
                Priority = Priority.P1Critical
            }
        };

        PriorityCalculator.Resolve(matrix, Impact.Minor, Urgency.Low)
            .ShouldBe(Priority.P1Critical);
    }

    [Fact]
    public void Combinations_absent_from_the_tenant_matrix_fall_back_to_the_default()
    {
        var matrix = new List<PriorityMatrixEntry>
        {
            new() { Impact = Impact.Minor, Urgency = Urgency.Low, Priority = Priority.P1Critical }
        };

        PriorityCalculator.Resolve(matrix, Impact.Extensive, Urgency.Critical)
            .ShouldBe(Priority.P1Critical);

        PriorityCalculator.Resolve(matrix, Impact.Moderate, Urgency.Medium)
            .ShouldBe(Priority.P3Moderate);
    }

    [Fact]
    public void An_empty_tenant_matrix_resolves_entirely_from_the_default()
    {
        foreach (var (impact, urgency, expected) in PriorityCalculator.DefaultMatrix())
        {
            PriorityCalculator.Resolve([], impact, urgency).ShouldBe(expected);
        }
    }
}
