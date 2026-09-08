namespace NexaOps.Domain.Common;

/// <summary>A fact that has already happened inside the domain.</summary>
public abstract record DomainEvent
{
    public Guid EventId { get; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}

/// <summary>Aggregate roots that raise domain events for the dispatcher to publish after commit.</summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<DomainEvent> DomainEvents { get; }
    void RaiseEvent(DomainEvent domainEvent);
    void ClearDomainEvents();
}
