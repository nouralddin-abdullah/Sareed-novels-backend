namespace Domain.Exceptions;

/// <summary>
/// Mapped to HTTP 404 by the error middleware: the message as plain text, or, with a <see cref="Code"/>, as JSON
/// <c>{"code", "message"}</c>.
/// </summary>
public class NotFoundException(string message, string? code = null) : Exception(message)
{
    /// <summary>A stable, English, PascalCase reason (e.g. "TargetNotFound"); null for the plain-text answer.</summary>
    public string? Code { get; } = code;
}
