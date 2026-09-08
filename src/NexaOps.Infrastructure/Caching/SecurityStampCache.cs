using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;

namespace NexaOps.Infrastructure.Caching;

/// <inheritdoc />
public sealed class SecurityStampCache : ISecurityStampCache
{
    /// <summary>
    /// How long a verified stamp is trusted without re-reading it.
    /// <para>
    /// This is the failure window, not the revocation delay: every path that rotates a stamp
    /// evicts this entry, so revocation is immediate. The window only bounds how stale an entry
    /// can get if an eviction is ever missed — which is why it is a minute and not an hour.
    /// </para>
    /// </summary>
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private readonly IDistributedCache _cache;
    private readonly ILogger<SecurityStampCache> _logger;

    public SecurityStampCache(IDistributedCache cache, ILogger<SecurityStampCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Keyed by user alone: an identifier is globally unique, so no tenant prefix.</summary>
    private static string Key(Guid userId) => $"nexaops:security-stamp:{userId:N}";

    /// <inheritdoc />
    public async Task<string?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _cache.GetStringAsync(Key(userId), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A miss costs a database read. An exception here would fail authentication for
            // every request, which is a far worse outcome than a slower one.
            _logger.LogWarning(ex, "Security stamp cache read failed; falling back to the database.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync(Guid userId, string stamp, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.SetStringAsync(
                    Key(userId),
                    stamp,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Window },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Security stamp cache write failed; the next request will re-read.");
        }
    }

    /// <inheritdoc />
    public async Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.RemoveAsync(Key(userId), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Logged at warning rather than swallowed: a failed eviction means a revoked session
            // survives until the window expires, and somebody should be able to see that it
            // happened.
            _logger.LogWarning(
                ex,
                "Security stamp eviction failed for {UserId}; revocation may take up to {Seconds}s.",
                userId,
                Window.TotalSeconds);
        }
    }
}
