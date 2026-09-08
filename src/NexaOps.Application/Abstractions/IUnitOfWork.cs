namespace NexaOps.Application.Abstractions;

/// <summary>
/// Commits the changes tracked by the current scope.
/// <para>
/// Application services mutate entities returned by repositories and then call
/// <see cref="SaveChangesAsync"/> exactly once, so that a use case that touches an incident,
/// its SLA clocks, a comment and a notification either lands completely or not at all.
/// </para>
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Persists all pending changes. Auditing, tenant stamping and tenant-isolation checks run
    /// inside this call, so nothing reaches the database without them.
    /// </summary>
    /// <returns>The number of state entries written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="action"/> inside an explicit database transaction, for the rare use
    /// case that needs more than one round trip to be atomic - allocating a record number and
    /// then inserting the record that uses it, for instance.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default);
}
