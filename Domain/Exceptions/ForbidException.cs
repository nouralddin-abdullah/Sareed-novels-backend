namespace Domain.Exceptions;

/// <summary>
/// Mapped to HTTP 403 by the error middleware, answered as JSON <c>{"code", "message"}</c>: the code lets clients tell
/// the reason apart without reading the message.
/// </summary>
public class ForbidException(string message, string? code = null) : Exception(message)
{
    /// <summary>A stable, English, PascalCase reason (e.g. "Blocked"); null answers with the generic "Forbidden".</summary>
    public string? Code { get; } = code;
}
