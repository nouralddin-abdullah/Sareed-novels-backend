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
}
