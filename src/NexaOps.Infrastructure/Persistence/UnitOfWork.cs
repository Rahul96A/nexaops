using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Common;

namespace NexaOps.Infrastructure.Persistence;

/// <inheritdoc />
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly NexaOpsDbContext _context;
    private readonly ILogger<UnitOfWork> _logger;

    public UnitOfWork(NexaOpsDbContext context, ILogger<UnitOfWork> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Someone else changed the row between our read and our write. Surfacing this as a
            // conflict lets the UI reload and show the difference, rather than silently losing
            // one of the two edits.
            _logger.LogWarning(ex, "Optimistic concurrency conflict during save.");

            throw new ConflictException(
                "concurrency.conflict",
                "This record was changed by someone else while you were editing it. " +
                "Reload the record and apply your changes again.");
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(ex, "Unique constraint violation during save.");

            throw new ConflictException(
                "constraint.duplicate",
                "A record with those details already exists.");
        }
    }

    /// <inheritdoc />
    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        // Nested calls join the ambient transaction rather than opening a second one.
        if (_context.Database.CurrentTransaction is not null)
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }

        // The execution strategy owns the retry loop, so a transient SQL failure retries the
        // whole transaction rather than replaying half of it.
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async ct =>
        {
            IDbContextTransaction? transaction = null;

            try
            {
                transaction = await _context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

                var result = await action(ct).ConfigureAwait(false);

                await _context.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);

                return result;
            }
            catch
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(ct).ConfigureAwait(false);
                }

                throw;
            }
            finally
            {
                if (transaction is not null)
                {
                    await transaction.DisposeAsync().ConfigureAwait(false);
                }
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Detects a duplicate-key failure across the providers we run on: SQL Server in every
    /// deployed environment, SQLite in the integration test suite.
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        if (exception.InnerException is Microsoft.Data.SqlClient.SqlException sql)
        {
            // 2601 is a unique index violation, 2627 a unique constraint violation.
            return sql.Errors.Cast<Microsoft.Data.SqlClient.SqlError>()
                .Any(e => e.Number is 2601 or 2627);
        }

        return exception.InnerException is not null
               && exception.InnerException.GetType().Name.Contains("Sqlite", StringComparison.Ordinal)
               && exception.InnerException.Message.Contains("UNIQUE constraint failed", StringComparison.Ordinal);
    }
}
