namespace NexaOps.Application.Abstractions;

/// <summary>
/// The clock, injected rather than called statically so that SLA behaviour can be tested at
/// specific instants without waiting for real time to pass.
/// </summary>
public interface IDateTimeProvider
{
    /// <summary>Current instant in UTC. Everything is stored in UTC; conversion happens at the edge.</summary>
    DateTimeOffset UtcNow { get; }
}
