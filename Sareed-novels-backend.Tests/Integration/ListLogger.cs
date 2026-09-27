using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Keeps what was logged, with the structured values: to check that failures are surfaced and what a summary line
/// reported. Thread-safe, since background workers log while a test reads.
/// </summary>
public sealed class ListLogger<T> : ILogger<T>
{
    public ConcurrentQueue<(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Values)> Entries { get; } = new();

    public IEnumerable<string> Warnings => Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var values = new Dictionary<string, object?>();
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var (key, value) in pairs)
            {
                values.TryAdd(key, value);
            }
        }
        Entries.Enqueue((logLevel, formatter(state, exception), values));
    }
}
