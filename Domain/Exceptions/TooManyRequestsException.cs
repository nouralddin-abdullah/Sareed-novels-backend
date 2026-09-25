namespace Domain.Exceptions;

/// <summary>Mapped to HTTP 429 by the error middleware.</summary>
public class TooManyRequestsException(string message) : Exception(message)
{
}
