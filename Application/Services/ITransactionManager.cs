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

    /// <summary>
    /// Runs <paramref name="work"/> in a transaction on the request's DbContext: joins the current transaction if one
    /// is open, otherwise begins one, commits when <paramref name="work"/> returns and rolls back if it throws.
    /// </summary>
    Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="InTransactionAsync{T}"/>
    Task InTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default);
}
