namespace Domain.Exceptions;

/// <summary>
/// Mapped to HTTP 429 by the error middleware, answered as JSON <c>{"code", "message"}</c>.
/// </summary>
public class TooManyRequestsException(string message, string? code = null) : Exception(message)
{
    /// <summary>A stable, English, PascalCase reason (e.g. "TooManyReports"); null answers with the generic "TooManyRequests".</summary>
    public string? Code { get; } = code;
}
