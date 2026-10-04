using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// The SQL commands the API sends while serving a request marked with <see cref="Header"/>: the request's own,
/// apart from anything running beside it (hosted services, other requests), told apart by the request each command
/// is sent for (IHttpContextAccessor's, which follows the request wherever it goes).
/// </summary>
internal sealed class CommandsByRequest : DbCommandInterceptor
{
    public const string Header = "X-Test-Measure";

    private readonly HttpContextAccessor requests = new();
    private readonly ConcurrentQueue<(string Marker, string Sql)> commands = new();

    public List<string> Of(string marker) => commands.Where(c => c.Marker == marker).Select(c => c.Sql).ToList();

    private void Log(DbCommand command)
    {
        var marker = requests.HttpContext?.Request.Headers[Header].ToString();
        if (!string.IsNullOrEmpty(marker))
        {
            commands.Enqueue((marker, command.CommandText));
        }
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Log(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Log(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Log(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Log(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}
