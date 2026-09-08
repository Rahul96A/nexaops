using NexaOps.Domain.Common;

namespace NexaOps.Domain.ServiceDesk;

/// <summary>
/// One cell of the configurable impact-by-urgency priority matrix. Each tenant gets a full
/// 4x4 grid seeded at provisioning time and may re-tune any cell from the admin UI without
/// a code change or deployment.
/// </summary>
public class PriorityMatrixEntry : TenantEntity
{
    public Impact Impact { get; set; }
    public Urgency Urgency { get; set; }
    public Priority Priority { get; set; }
}

/// <summary>
/// Derives priority from impact and urgency.
/// <para>
/// The tenant matrix always wins. <see cref="DefaultFor"/> exists so that a tenant whose
/// matrix has not finished seeding still gets a sensible, deterministic answer rather than
/// a null - it is the fallback, never a silent override of customer configuration.
/// </para>
/// </summary>
public static class PriorityCalculator
{
    /// <summary>
    /// Industry-conventional 4x4 mapping. Impact and urgency are both ranked 1 (worst) to
    /// 4 (least severe), so their sum ranks severity: 2 is the most severe pair and 8 the least.
    /// </summary>
    public static Priority DefaultFor(Impact impact, Urgency urgency)
        => ((int)impact + (int)urgency) switch
        {
            <= 2 => Priority.P1Critical,   // Extensive + Critical
            3 => Priority.P2High,
            4 => Priority.P2High,
            5 => Priority.P3Moderate,
            6 => Priority.P3Moderate,
            7 => Priority.P4Low,
            _ => Priority.P5Planning       // Minor + Low
        };

    /// <summary>
    /// Resolves priority against a tenant matrix, falling back to <see cref="DefaultFor"/>
    /// when the tenant has no entry for this combination.
    /// </summary>
    public static Priority Resolve(
        IEnumerable<PriorityMatrixEntry> tenantMatrix,
        Impact impact,
        Urgency urgency)
    {
        ArgumentNullException.ThrowIfNull(tenantMatrix);

        var match = tenantMatrix.FirstOrDefault(e => e.Impact == impact && e.Urgency == urgency);
        return match?.Priority ?? DefaultFor(impact, urgency);
    }

    /// <summary>Produces the full default grid, used when provisioning a new tenant.</summary>
    public static IEnumerable<(Impact Impact, Urgency Urgency, Priority Priority)> DefaultMatrix()
    {
        foreach (var impact in Enum.GetValues<Impact>())
        {
            foreach (var urgency in Enum.GetValues<Urgency>())
            {
                yield return (impact, urgency, DefaultFor(impact, urgency));
            }
        }
    }
}
