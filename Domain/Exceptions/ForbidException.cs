namespace Domain.Exceptions;

public class ForbidException(string message) : Exception(message)
{
}
