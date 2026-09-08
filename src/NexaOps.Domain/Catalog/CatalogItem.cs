using NexaOps.Domain.Approvals;
using NexaOps.Domain.Common;
using NexaOps.Domain.Identity;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Domain.Catalog;

/// <summary>
/// Something a person can order from the service catalogue: a laptop, a software licence, VPN
/// access, a new starter setup.
/// <para>
/// A catalogue item is a <em>template</em>. It declares what is being offered, what must be
/// filled in to order it, who fulfils it, and whether it needs authorising. Ordering one
/// produces a <c>RequestItem</c>, which snapshots the answers - so editing the template later
/// never rewrites what somebody already ordered.
/// </para>
/// </summary>
public class CatalogItem : TenantEntity
{
    /// <summary>Stable identifier used in configuration and imports, e.g. <c>LAPTOP-STD</c>.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>One line shown on the catalogue tile.</summary>
    public string ShortDescription { get; set; } = string.Empty;

    /// <summary>Full description shown on the item page.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Classification, sharing the taxonomy with incidents via the Module discriminator.</summary>
    public Guid? CategoryId { get; set; }

    public CatalogItemStatus Status { get; set; } = CatalogItemStatus.Draft;

    /// <summary>Group that fulfils orders for this item.</summary>
    public Guid? FulfilmentGroupId { get; set; }

    /// <summary>
    /// Priority given to requests for this item. Requests do not derive priority from impact and
    /// urgency the way incidents do - a request is planned work, and what matters is what was
    /// ordered, not how badly something is broken.
    /// </summary>
    public Priority Priority { get; set; } = Priority.P4Low;

    // --- Approval configuration ---

    /// <summary>When true, ordering this item raises approvals before fulfilment can start.</summary>
    public bool RequiresApproval { get; set; }

    /// <summary>Who must authorise. Ignored when <see cref="RequiresApproval"/> is false.</summary>
    public ApprovalTargetKind ApprovalTargetKind { get; set; } = ApprovalTargetKind.Manager;

    /// <summary>Named approver, when <see cref="ApprovalTargetKind"/> is User.</summary>
    public Guid? ApproverUserId { get; set; }

    /// <summary>Approving group, when <see cref="ApprovalTargetKind"/> is Group.</summary>
    public Guid? ApproverGroupId { get; set; }

    /// <summary>How multiple approvers in the stage combine.</summary>
    public ApprovalRule ApprovalRule { get; set; } = ApprovalRule.AnyOne;

    // --- Presentation and commercials ---

    /// <summary>Indicative cost in the tenant's currency. Display only; nothing bills from this.</summary>
    public decimal? Cost { get; set; }

    /// <summary>Working days typically needed, shown to set expectations before ordering.</summary>
    public int? EstimatedDeliveryDays { get; set; }

    /// <summary>Material Symbols icon name, so the catalogue reads visually.</summary>
    public string? Icon { get; set; }

    /// <summary>Ordering within a category. Lower sorts first.</summary>
    public int SortOrder { get; set; }

    /// <summary>Maximum quantity per order line. Null means unconstrained.</summary>
    public int? MaxQuantity { get; set; }

    // --- Navigation ---
    public Category? Category { get; set; }
    public Group? FulfilmentGroup { get; set; }
    public ICollection<CatalogItemVariable> Variables { get; set; } = new List<CatalogItemVariable>();

    /// <summary>True when requesters can currently see and order this.</summary>
    public bool IsOrderable => Status == CatalogItemStatus.Published && !IsArchived;

    /// <summary>
    /// Publishes the item, refusing if it is not actually ready.
    /// <para>
    /// Publishing without a fulfilment group would put orders into a queue nobody owns, which
    /// surfaces to the requester as silence. Better to refuse here than to accept an order the
    /// business cannot deliver.
    /// </para>
    /// </summary>
    public void Publish()
    {
        if (FulfilmentGroupId is null)
        {
            throw new DomainException(
                "catalog.no_fulfilment_group",
                "A catalogue item needs a fulfilment group before it can be published.");
        }

        if (RequiresApproval
            && ApprovalTargetKind == ApprovalTargetKind.User
            && ApproverUserId is null)
        {
            throw new DomainException(
                "catalog.no_approver",
                "This item is configured to need a named approver, but no approver is set.");
        }

        if (RequiresApproval
            && ApprovalTargetKind == ApprovalTargetKind.Group
            && ApproverGroupId is null)
        {
            throw new DomainException(
                "catalog.no_approver",
                "This item is configured to need an approving group, but no group is set.");
        }

        Status = CatalogItemStatus.Published;
    }

    /// <summary>
    /// Withdraws the item from the catalogue. Existing requests are unaffected - they hold their
    /// own snapshot of what was ordered.
    /// </summary>
    public void Retire() => Status = CatalogItemStatus.Retired;
}
