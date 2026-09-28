namespace Domain.Exceptions;

/// <summary>
/// Mapped to HTTP 404 by the error middleware, answered as JSON <c>{"code", "message"}</c>.
/// </summary>
public class NotFoundException(string message, string? code = null) : Exception(message)
{
    /// <summary>A stable, English, PascalCase reason (e.g. "TargetNotFound"); null answers with the generic "NotFound".</summary>
    public string? Code { get; } = code;
}
