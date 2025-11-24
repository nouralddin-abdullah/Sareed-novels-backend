using Microsoft.EntityFrameworkCore.Storage;

namespace Application.Services;

/// <summary>
/// Provides database transaction management for atomic operations.
/// Use this to ensure multiple operations succeed or fail together.
/// </summary>
public interface ITransactionManager
{
    /// <summary>
    /// Begins a new database transaction.
    /// Must be committed or rolled back before disposal.
    /// </summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
