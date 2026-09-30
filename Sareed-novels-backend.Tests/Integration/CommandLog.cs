using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The SQL of every command a context sends (<see cref="SqlServerDatabase.CreateContext(IInterceptor[])"/>), to check
/// that a page is the same few queries however many items it has.
/// </summary>
internal sealed class CommandLog : DbCommandInterceptor
{
    public ConcurrentQueue<string> Commands { get; } = new();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Commands.Enqueue(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Commands.Enqueue(command.CommandText);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Commands.Enqueue(command.CommandText);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}
