namespace NexaOps.Application.Abstractions;

/// <summary>
/// Caches the security stamp the API compares against on every authenticated request, and — the
/// part that matters — evicts it the moment the stamp changes.
/// <para>
/// Separate from <see cref="IApplicationCache"/> on purpose. That cache prefixes every key with
/// the ambient tenant, and stamp validation runs <em>before</em> a tenant scope exists: the
/// validator would write under the global prefix while a revocation running inside a tenant
/// scope evicted a differently-prefixed key. The eviction would look correct in the code and do
/// nothing at all, which is the worst possible outcome for a control whose entire purpose is to
/// cut access immediately.
/// </para>
/// <para>
/// A user identifier is globally unique, so this cache is keyed by it alone.
/// </para>
/// </summary>
public interface ISecurityStampCache
{
    Task<string?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    Task SetAsync(Guid userId, string stamp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops the cached stamp, so the next request re-reads it.
    /// <para>
    /// Must be called wherever a stamp is rotated — a password change, a role change, an account
    /// being disabled. Without it, revocation waits out the cache window, and "takes effect
    /// immediately" becomes "takes effect within a minute", which is a different promise.
    /// </para>
    /// </summary>
    Task InvalidateAsync(Guid userId, CancellationToken cancellationToken = default);
}
