namespace NexaOps.Domain.Common;

/// <summary>Entities whose create/update provenance is stamped automatically on save.</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    DateTimeOffset? UpdatedAt { get; set; }
    Guid? UpdatedBy { get; set; }
}

/// <summary>Entities that are archived rather than deleted, to preserve audit integrity.</summary>
public interface ISoftArchivable
{
    bool IsArchived { get; set; }
    DateTimeOffset? ArchivedAt { get; set; }
    Guid? ArchivedBy { get; set; }
}

/// <summary>Entities protected by optimistic concurrency using a SQL Server rowversion.</summary>
public interface IConcurrencyAware
{
    byte[]? RowVersion { get; set; }
}
