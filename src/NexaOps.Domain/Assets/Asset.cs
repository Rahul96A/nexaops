using NexaOps.Domain.Cmdb;
using NexaOps.Domain.Common;
using NexaOps.Domain.Identity;

namespace NexaOps.Domain.Assets;

/// <summary>Where an asset is in its working life.</summary>
public enum AssetStatus
{
    /// <summary>Ordered but not yet received.</summary>
    OnOrder = 1,

    /// <summary>Received and available to issue.</summary>
    InStock = 2,

    /// <summary>Issued to somebody.</summary>
    Assigned = 3,

    /// <summary>Away being fixed. Still owned, still on the books.</summary>
    InRepair = 4,

    /// <summary>Withdrawn from use but retained.</summary>
    Retired = 5,

    /// <summary>Gone: sold, scrapped or written off.</summary>
    Disposed = 6,

    /// <summary>Unaccounted for. Deliberately distinct from disposed.</summary>
    Lost = 7
}

/// <summary>Broad class of asset. Drives which fields matter and how it depreciates.</summary>
public enum AssetKind
{
    Hardware = 1,
    Software = 2,
    Peripheral = 3,
    Mobile = 4,
    Consumable = 5
}

/// <summary>
/// A thing the organisation owns and is accountable for.
/// <para>
/// Distinct from a configuration item, and deliberately so. A CI answers "what does this support
/// and what does it depend on"; an asset answers "who has it, what did it cost, and when does it
/// need replacing". The same laptop is often both, so the two are linked rather than merged -
/// merging them produces a record that serves neither question well.
/// </para>
/// </summary>
public class Asset : TenantEntity
{
    /// <summary>Human-facing identifier, e.g. AST0000042. Unique per tenant, never reused.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>The label physically stuck on it. What a finance audit actually counts.</summary>
    public string AssetTag { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public AssetKind Kind { get; set; } = AssetKind.Hardware;
    public AssetStatus Status { get; set; } = AssetStatus.InStock;

    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }

    // --- Custody ---

    /// <summary>Who currently holds it. Null unless the status is Assigned.</summary>
    public Guid? AssignedToUserId { get; set; }

    public DateTimeOffset? AssignedAt { get; set; }

    /// <summary>Where it is, e.g. <c>Pune office, 3rd floor</c>.</summary>
    public string? Location { get; set; }

    // --- Commercials ---
    public DateOnly? PurchasedOn { get; set; }

    /// <summary>What it cost, in the tenant's currency.</summary>
    public decimal? PurchaseCost { get; set; }

    public string? Vendor { get; set; }
    public string? PurchaseOrderNumber { get; set; }

    public DateOnly? WarrantyExpiresOn { get; set; }

    /// <summary>
    /// Expected working life. Combined with the purchase date this gives a refresh date, which
    /// is the number a budget holder actually plans against.
    /// </summary>
    public int? UsefulLifeMonths { get; set; }

    // --- Links ---

    /// <summary>The configuration item this asset is, where it is also part of the estate.</summary>
    public Guid? ConfigurationItemId { get; set; }

    /// <summary>Note recorded when the asset is disposed of or written off.</summary>
    public string? DisposalNotes { get; set; }

    public DateOnly? DisposedOn { get; set; }

    // --- Navigation ---
    public User? AssignedTo { get; set; }
    public ConfigurationItem? ConfigurationItem { get; set; }

    public ICollection<AssetAssignment> AssignmentHistory { get; set; } = new List<AssetAssignment>();

    /// <summary>True while the organisation still has it and can use it.</summary>
    public bool IsInService => Status is AssetStatus.InStock or AssetStatus.Assigned or AssetStatus.InRepair;

    /// <summary>True when warranty cover has lapsed as at the given date.</summary>
    public bool IsOutOfWarranty(DateOnly asOf)
        => WarrantyExpiresOn is not null && WarrantyExpiresOn < asOf;

    /// <summary>
    /// When this is expected to need replacing, or null when there is not enough information.
    /// <para>
    /// Null rather than a guess: a refresh date invented from a missing purchase date would be
    /// worse than no date at all, because somebody would budget against it.
    /// </para>
    /// </summary>
    public DateOnly? RefreshDueOn =>
        PurchasedOn is null || UsefulLifeMonths is null or <= 0
            ? null
            : PurchasedOn.Value.AddMonths(UsefulLifeMonths.Value);

    /// <summary>True when the asset is past its expected working life.</summary>
    public bool IsDueForRefresh(DateOnly asOf)
        => RefreshDueOn is not null && RefreshDueOn <= asOf;

    // ------------------------------------------------------------------
    // Behaviour
    // ------------------------------------------------------------------

    /// <summary>
    /// Issues the asset to somebody, closing any open custody record first.
    /// </summary>
    public AssetAssignment AssignTo(Guid userId, DateTimeOffset now, string? note)
    {
        if (!IsInService)
        {
            throw new DomainException(
                "asset.not_in_service",
                $"An asset that is {Status} cannot be issued to anyone.");
        }

        CloseOpenCustody(now, "Reassigned.");

        AssignedToUserId = userId;
        AssignedAt = now;
        Status = AssetStatus.Assigned;

        var assignment = new AssetAssignment
        {
            TenantId = TenantId,
            AssetId = Id,
            UserId = userId,
            AssignedAt = now,
            AssignmentNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };

        AssignmentHistory.Add(assignment);
        return assignment;
    }

    /// <summary>
    /// Takes the asset back, closing the custody record. The history is what makes an asset
    /// register defensible: "who had this in March" has to be answerable.
    /// </summary>
    public void Return(DateTimeOffset now, string? note, AssetStatus returnTo = AssetStatus.InStock)
    {
        if (AssignedToUserId is null)
        {
            throw new DomainException(
                "asset.not_assigned",
                "This asset is not currently issued to anyone.");
        }

        CloseOpenCustody(now, note);

        AssignedToUserId = null;
        AssignedAt = null;
        Status = returnTo;
    }

    /// <summary>Records disposal, which is terminal for the asset's working life.</summary>
    public void Dispose(DateOnly disposedOn, string? notes, DateTimeOffset now)
    {
        // Custody is closed first: an asset disposed of while still showing somebody as holding
        // it leaves that person apparently accountable for something that no longer exists.
        if (AssignedToUserId is not null)
        {
            CloseOpenCustody(now, "Asset disposed.");
            AssignedToUserId = null;
            AssignedAt = null;
        }

        Status = AssetStatus.Disposed;
        DisposedOn = disposedOn;
        DisposalNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    private void CloseOpenCustody(DateTimeOffset now, string? note)
    {
        foreach (var open in AssignmentHistory.Where(a => a.ReturnedAt is null))
        {
            open.Close(now, note);
        }
    }
}

/// <summary>
/// One period during which a person held an asset.
/// <para>
/// Kept as history rather than only as a current holder, because "who had this laptop in March"
/// is the question an audit or a security incident actually asks, and a single mutable field
/// cannot answer it.
/// </para>
/// </summary>
public class AssetAssignment : TenantEntity
{
    public Guid AssetId { get; set; }
    public Guid UserId { get; set; }

    public DateTimeOffset AssignedAt { get; set; }

    /// <summary>Null while the person still holds it.</summary>
    public DateTimeOffset? ReturnedAt { get; set; }

    public string? AssignmentNote { get; set; }
    public string? ReturnNote { get; set; }

    public Asset? Asset { get; set; }
    public User? User { get; set; }

    /// <summary>True while this is the live custody record.</summary>
    public bool IsOpen => ReturnedAt is null;

    /// <summary>How long they held it, or how long they have so far.</summary>
    public TimeSpan HeldFor(DateTimeOffset asOf) => (ReturnedAt ?? asOf) - AssignedAt;

    /// <summary>Closes the custody period.</summary>
    public void Close(DateTimeOffset now, string? note)
    {
        if (ReturnedAt is not null)
        {
            return;
        }

        ReturnedAt = now;
        ReturnNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
