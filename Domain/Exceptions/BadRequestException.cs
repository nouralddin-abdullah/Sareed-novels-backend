namespace Domain.Exceptions;

/// <summary>
/// A request the API refuses as it stands (HTTP 400), answered by the error middleware as JSON
/// <c>{"code", "message"}</c>: <see cref="Code"/> is stable for clients to branch on, the message is Arabic for people.
/// </summary>
public class BadRequestException(string message, string code) : Exception(message)
{
    public string Code { get; } = code;
}
