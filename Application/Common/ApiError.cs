namespace Application.Common;

/// <summary>
/// The API's error body, JSON <c>{"code", "message"}</c>, for errors a controller answers itself (the error middleware
/// writes the same shape for exceptions): <see cref="Code"/> is stable for clients to branch on, <see cref="Message"/>
/// is Arabic, for people.
/// </summary>
public sealed record ApiError(string Code, string Message);
