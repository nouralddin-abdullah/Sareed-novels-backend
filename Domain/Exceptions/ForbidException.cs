namespace Domain.Exceptions;

/// <summary>
/// Mapped to HTTP 403 by the error middleware: the message as plain text, or, with a <see cref="Code"/>, as JSON
/// <c>{"code", "message"}</c> so clients can tell the reason apart without reading the (Arabic) message.
/// </summary>
public class ForbidException(string message, string? code = null) : Exception(message)
{
    /// <summary>A stable, English, PascalCase reason (e.g. "Blocked"); null for the plain-text answer.</summary>
    public string? Code { get; } = code;
}
