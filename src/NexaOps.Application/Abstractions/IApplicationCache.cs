namespace NexaOps.Application.Abstractions;

/// <summary>
/// Distributed cache port. In-memory locally, Azure Cache for Redis in Azure.
/// <para>
/// The implementation prefixes every key with the ambient tenant, so a cache key collision
/// between tenants is structurally impossible rather than merely unlikely.
/// </para>
/// </summary>
public interface IApplicationCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    Task SetAsync<T>(string key, T value, TimeSpan? absoluteExpiry = null, CancellationToken cancellationToken = default)
        where T : class;

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Reads through to <paramref name="factory"/> on a miss and caches the result.</summary>
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? absoluteExpiry = null,
        CancellationToken cancellationToken = default) where T : class;
}
