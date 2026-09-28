namespace Domain.Exceptions;

/// <summary>
/// A request that conflicts with what is already so (HTTP 409), answered by the error middleware as JSON
/// <c>{"code", "message"}</c>; e.g. deleting an account that is deleted already.
/// </summary>
public class ConflictException(string message, string code) : Exception(message)
{
    /// <summary>A stable, English, PascalCase reason (e.g. "AlreadyDeleted").</summary>
    public string Code { get; } = code;
}
