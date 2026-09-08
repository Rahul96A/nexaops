using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Abstractions;
using NexaOps.Infrastructure.Persistence;

namespace NexaOps.Api.Identity;

/// <summary>
/// Confirms that the security stamp embedded in an access token still matches the one stored
/// for that user.
/// <para>
/// This is what makes a permission or password change take effect immediately rather than at
/// token expiry. The lookup is cached briefly so it costs a database round trip only once per
/// user per cache window, not once per request.
/// </para>
/// </summary>
public sealed class SecurityStampValidator
{
    /// <summary>
    /// How long a verified stamp is trusted. Short enough that a revocation is felt almost at
    /// once, long enough that a busy agent does not hit the database on every call.
    /// </summary>
    private static readonly TimeSpan CacheWindow = TimeSpan.FromSeconds(60);

    private readonly IDbContextFactory<NexaOpsDbContext> _contextFactory;
    private readonly IApplicationCache _cache;
    private readonly ILogger<SecurityStampValidator> _logger;

    public SecurityStampValidator(
        IDbContextFactory<NexaOpsDbContext> contextFactory,
        IApplicationCache cache,
        ILogger<SecurityStampValidator> logger)
    {
        _contextFactory = contextFactory;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>True when the presented stamp is the user's current one.</summary>
    public async Task<bool> IsCurrentAsync(
        Guid userId,
        string presentedStamp,
        CancellationToken cancellationToken = default)
    {
        var key = $"security-stamp:{userId:N}";

        var current = await _cache.GetAsync<StampEnvelope>(key, cancellationToken).ConfigureAwait(false);

        if (current is null)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            // Runs before a tenant scope exists, so the tenant filter is widened for this one
            // lookup. It is keyed on a primary key and returns a single opaque value.
            using var _ = context.SuppressTenantFilter();

            var stamp = await context.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.SecurityStamp, u.Status, u.IsArchived })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (stamp is null || stamp.IsArchived || stamp.Status != Domain.Identity.UserStatus.Active)
            {
                _logger.LogInformation(
                    "Rejected a token for user {UserId}: the account is missing, archived or disabled.",
                    userId);
                return false;
            }

            current = new StampEnvelope(stamp.SecurityStamp);
            await _cache.SetAsync(key, current, CacheWindow, cancellationToken).ConfigureAwait(false);
        }

        var matches = string.Equals(current.Value, presentedStamp, StringComparison.Ordinal);

        if (!matches)
        {
            _logger.LogInformation(
                "Rejected a token for user {UserId}: the security stamp has changed.",
                userId);
        }

        return matches;
    }

    /// <summary>Wrapper so the cache stores a reference type, as its contract requires.</summary>
    public sealed record StampEnvelope(string Value);
}
