using Application.Services;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class TransactionManager(ApplicationDbContext dbContext, ILogger<TransactionManager>? logger = null) : ITransactionManager
{
    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction != null)
        {
            return await work();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await work();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception failure)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollback)
            {
                // SQL Server may have ended the transaction itself (a deadlock victim is rolled back whole): the failure
                // that caused it is the one to report.
                logger?.LogDebug(rollback, "Rolling back after {Failure} failed too", failure.GetType().Name);
            }
            throw;
        }
    }

    public Task InTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            await work();
            return true;
        }, cancellationToken);

    public async Task<T> InNewTransactionAsync<T>(Func<int, Task<T>> work, int attempts = 1, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction != null)
        {
            throw new InvalidOperationException("A transaction that may be run again can't be part of another one");
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var current = attempt;
                return await InTransactionAsync(() => work(current), cancellationToken);
            }
            catch (Exception ex)
            {
                // Rolled back: what the attempt added to the context (saved or about to be) isn't in the database, and a
                // later SaveChanges must not write it.
                dbContext.ChangeTracker.Clear();
                if (attempt >= attempts || !SqlErrors.IsDeadlock(ex))
                {
                    throw;
                }
                logger?.LogWarning(ex, "Picked as a deadlock victim (attempt {Attempt} of {Attempts}): running it again in a new transaction",
                    attempt, attempts);
            }
        }
    }
}
