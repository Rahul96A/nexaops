using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;

namespace NexaOps.Infrastructure.Caching;

/// <summary>
/// Distributed cache over <see cref="IDistributedCache"/> - in-memory locally, Azure Cache for
/// Redis in Azure.
/// <para>
/// Every key is prefixed with the ambient tenant id. Cross-tenant cache poisoning is therefore
/// structurally impossible rather than merely unlikely, and a caller cannot craft a key that
/// escapes its tenant because it never supplies the prefix.
/// </para>
/// </summary>
public sealed class ApplicationCache : IApplicationCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(10);

    private readonly IDistributedCache _cache;
    private readonly ITenantContext _tenant;
    private readonly ILogger<ApplicationCache> _logger;

    public ApplicationCache(IDistributedCache cache, ITenantContext tenant, ILogger<ApplicationCache> logger)
    {
        _cache = cache;
        _tenant = tenant;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var payload = await _cache.GetStringAsync(Scoped(key), cancellationToken).ConfigureAwait(false);
            return payload is null ? null : JsonSerializer.Deserialize<T>(payload, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A cache is an optimisation. If Redis is unreachable the request should be slower,
            // not broken, so a miss is reported rather than an exception propagated.
            _logger.LogWarning(ex, "Cache read failed for key {Key}; treating as a miss.", key);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? absoluteExpiry = null,
        CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            await _cache.SetStringAsync(
                Scoped(key),
                JsonSerializer.Serialize(value, JsonOptions),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = absoluteExpiry ?? DefaultExpiry
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache write failed for key {Key}; continuing without caching.", key);
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.RemoveAsync(Scoped(key), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache eviction failed for key {Key}.", key);
        }
    }

    /// <inheritdoc />
    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? absoluteExpiry = null,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);

        var cached = await GetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var value = await factory(cancellationToken).ConfigureAwait(false);
        await SetAsync(key, value, absoluteExpiry, cancellationToken).ConfigureAwait(false);

        return value;
    }

    /// <summary>Prefixes the key with the ambient tenant, or <c>global</c> when none is established.</summary>
    private string Scoped(string key)
        => _tenant.HasTenant ? $"nexaops:{_tenant.TenantId:N}:{key}" : $"nexaops:global:{key}";
}
