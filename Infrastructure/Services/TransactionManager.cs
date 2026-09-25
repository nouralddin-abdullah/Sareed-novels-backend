using Application.Services;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace Infrastructure.Services;

public class TransactionManager(ApplicationDbContext dbContext) : ITransactionManager
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
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public Task InTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default) =>
        InTransactionAsync(async () =>
        {
            await work();
            return true;
        }, cancellationToken);
}
