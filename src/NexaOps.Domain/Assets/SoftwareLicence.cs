using NexaOps.Domain.Common;

namespace NexaOps.Domain.Assets;

/// <summary>How a licence is counted, which decides what "compliant" means.</summary>
public enum LicenceModel
{
    /// <summary>One entitlement per named person, whether or not they are using it today.</summary>
    PerUser = 1,

    /// <summary>One entitlement per machine it is installed on.</summary>
    PerDevice = 2,

    /// <summary>Entitlements are a pool; only concurrent use counts.</summary>
    Concurrent = 3,

    /// <summary>Unlimited within the organisation. Counted for cost, not for compliance.</summary>
    SiteLicence = 4
}

/// <summary>The organisation's position against a licence agreement.</summary>
public enum ComplianceState
{
    /// <summary>Using fewer entitlements than held.</summary>
    UnderUsed = 1,

    /// <summary>Using what is held, within tolerance.</summary>
    Compliant = 2,

    /// <summary>Using more than is held. The state that costs money in an audit.</summary>
    OverDeployed = 3,

    /// <summary>The agreement has lapsed. Every deployment against it is unlicensed.</summary>
    Expired = 4
}

/// <summary>
/// A software licence agreement and the organisation's position against it.
/// <para>
/// The module exists to answer one question honestly: are we over-deployed, and by how much. A
/// register that cannot say so is decoration, so the compliance position is computed from the
/// numbers rather than stored as a field somebody remembers to update.
/// </para>
/// </summary>
public class SoftwareLicence : TenantEntity
{
    /// <summary>Human-facing identifier, e.g. LIC0000042.</summary>
    public string Number { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string? Publisher { get; set; }
    public string? Version { get; set; }

    /// <summary>Agreement or contract reference, as the vendor knows it.</summary>
    public string? AgreementReference { get; set; }

    public LicenceModel Model { get; set; } = LicenceModel.PerUser;

    /// <summary>How many entitlements were bought.</summary>
    public int EntitlementCount { get; set; }

    /// <summary>
    /// How many are actually in use.
    /// <para>
    /// Maintained by whoever knows - an inventory feed, or a person. NexaOps does not discover
    /// installations itself, and pretending otherwise would make the compliance position a
    /// fiction. See docs/STATUS.md.
    /// </para>
    /// </summary>
    public int DeployedCount { get; set; }

    public DateOnly? StartsOn { get; set; }

    /// <summary>When the agreement lapses. Null for a perpetual licence.</summary>
    public DateOnly? ExpiresOn { get; set; }

    public decimal? AnnualCost { get; set; }
    public string? Vendor { get; set; }
    public string? Notes { get; set; }

    /// <summary>Entitlements left, or null where the model does not count them.</summary>
    public int? AvailableEntitlements =>
        Model == LicenceModel.SiteLicence ? null : EntitlementCount - DeployedCount;

    /// <summary>
    /// How many deployments exceed entitlement, or zero when within cover. The number that
    /// appears on an audit finding.
    /// </summary>
    public int OverDeployedBy =>
        Model == LicenceModel.SiteLicence ? 0 : Math.Max(0, DeployedCount - EntitlementCount);

    /// <summary>True when the agreement has lapsed as at the given date.</summary>
    public bool HasExpired(DateOnly asOf) => ExpiresOn is not null && ExpiresOn < asOf;

    /// <summary>
    /// The compliance position, computed rather than stored.
    /// <para>
    /// Expiry is checked first and beats everything else: an agreement that has lapsed makes
    /// every deployment against it unlicensed, however comfortable the seat count looks.
    /// </para>
    /// </summary>
    public ComplianceState ComplianceAt(DateOnly asOf)
    {
        if (HasExpired(asOf))
        {
            return ComplianceState.Expired;
        }

        if (Model == LicenceModel.SiteLicence)
        {
            return ComplianceState.Compliant;
        }

        if (DeployedCount > EntitlementCount)
        {
            return ComplianceState.OverDeployed;
        }

        // A licence used at 80% or more is "compliant" rather than under-used: flagging it as
        // spare capacity would invite somebody to cancel seats they are about to need.
        return EntitlementCount > 0 && DeployedCount * 100 / EntitlementCount >= 80
            ? ComplianceState.Compliant
            : ComplianceState.UnderUsed;
    }

    /// <summary>
    /// Records a change in how many deployments exist.
    /// </summary>
    public void SetDeployedCount(int count)
    {
        if (count < 0)
        {
            throw new DomainException(
                "licence.negative_deployment",
                "The number of deployments cannot be negative.");
        }

        DeployedCount = count;
    }

    /// <summary>Records a change to the entitlements held, e.g. after buying more seats.</summary>
    public void SetEntitlementCount(int count)
    {
        if (count < 0)
        {
            throw new DomainException(
                "licence.negative_entitlement",
                "The number of entitlements cannot be negative.");
        }

        EntitlementCount = count;
    }
}
