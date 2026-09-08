using NexaOps.Application.Abstractions;

namespace NexaOps.Infrastructure.Common;

/// <summary>The real clock. Tests substitute a fixed provider to pin SLA behaviour to an instant.</summary>
public sealed class SystemClock : IDateTimeProvider
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Correlation context for work that does not originate from an HTTP request - background
/// workers, seeding, and tests. The web host replaces this with one backed by the request.
/// </summary>
public sealed class BackgroundCorrelationContext : ICorrelationContext
{
    /// <inheritdoc />
    public string CorrelationId { get; } = "bg-" + Guid.CreateVersion7().ToString("N")[..16];

    /// <inheritdoc />
    public string? IpAddress => null;

    /// <inheritdoc />
    public string? UserAgent => "NexaOps.Background";
}
