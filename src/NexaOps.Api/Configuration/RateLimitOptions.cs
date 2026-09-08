using System.ComponentModel.DataAnnotations;

namespace NexaOps.Api.Configuration;

/// <summary>
/// Request rate limits.
/// <para>
/// These are configuration rather than constants because the right numbers depend on the
/// deployment: a single-tenant pilot behind a corporate proxy presents very differently from a
/// multi-tenant production estate behind Front Door. The defaults here are the production
/// intent, and an operator tunes them per environment without a code change.
/// </para>
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Requests per minute for general API traffic, partitioned per authenticated user and
    /// per source address for anonymous callers, so one noisy tenant cannot exhaust another's
    /// budget.
    /// </summary>
    [Range(10, 100_000)]
    public int GeneralRequestsPerMinute { get; set; } = 300;

    /// <summary>
    /// Sign-in attempts allowed per source address inside <see cref="AuthenticationWindowMinutes"/>.
    /// Deliberately tight: this is the endpoint an attacker uses to guess passwords.
    /// </summary>
    [Range(3, 10_000)]
    public int AuthenticationAttempts { get; set; } = 10;

    [Range(1, 120)]
    public int AuthenticationWindowMinutes { get; set; } = 5;

    /// <summary>
    /// AI requests per minute per user. Lower than general traffic because each one costs real
    /// money at the provider and is far more expensive to serve.
    /// </summary>
    [Range(1, 10_000)]
    public int AiRequestsPerMinute { get; set; } = 20;
}
