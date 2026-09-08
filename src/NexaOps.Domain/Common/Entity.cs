namespace NexaOps.Domain.Common;

/// <summary>
/// Base type for every persisted entity. Identifiers are UUID v7 so that they are
/// globally unique yet time-ordered, which keeps the clustered index from fragmenting
/// the way random UUID v4 keys do on SQL Server.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public override bool Equals(object? obj)
        => obj is Entity other && other.GetType() == GetType() && other.Id == Id && Id != Guid.Empty;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
