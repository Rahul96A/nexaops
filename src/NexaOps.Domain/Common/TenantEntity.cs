namespace NexaOps.Domain.Common;

/// <summary>
/// The standard base class for business records: tenant-owned, audited, concurrency-checked
/// and soft-archivable. Everything a customer can see or edit derives from this.
/// </summary>
public abstract class TenantEntity : Entity, ITenantOwned, IAuditable, IConcurrencyAware, ISoftArchivable
{
    public Guid TenantId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public Guid? ArchivedBy { get; set; }

    public byte[]? RowVersion { get; set; }
}
