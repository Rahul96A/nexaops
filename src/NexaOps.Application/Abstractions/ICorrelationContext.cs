namespace NexaOps.Application.Abstractions;

/// <summary>
/// Correlation identity for the current request or background job, stamped onto logs, traces
/// and audit rows so one incident can be traced end to end across all three.
/// </summary>
public interface ICorrelationContext
{
    string CorrelationId { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
}
