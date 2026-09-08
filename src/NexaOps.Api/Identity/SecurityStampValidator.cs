using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Abstractions;
using NexaOps.Infrastructure.Persistence;

namespace NexaOps.Api.Identity;

/// <summary>
/// Confirms that the security stamp embedded in an access token still matches the one stored
/// for that user.
/// <para>
/// This is what makes a permission or password change take effect immediately rather than at
/// token expiry. The lookup is cached so it costs a database round trip only once per user per
/// cache window rather than once per request — and every path that rotates a stamp evicts that
/// entry, which is what keeps "immediately" true.
/// </para>
/// </summary>
public sealed class SecurityStampValidator
{
    private readonly IDbContextFactory<NexaOpsDbContext> _contextFactory;
    private readonly ISecurityStampCache _cache;
    private readonly ILogger<SecurityStampValidator> _logger;

    public SecurityStampValidator(
        IDbContextFactory<NexaOpsDbContext> contextFactory,
        ISecurityStampCache cache,
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
        var current = await _cache.GetAsync(userId, cancellationToken).ConfigureAwait(false);

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

            current = stamp.SecurityStamp;
            await _cache.SetAsync(userId, current, cancellationToken).ConfigureAwait(false);
        }

        var matches = string.Equals(current, presentedStamp, StringComparison.Ordinal);

        if (!matches)
        {
            _logger.LogInformation(
                "Rejected a token for user {UserId}: the security stamp has changed.",
                userId);
        }

        return matches;
    }
}
