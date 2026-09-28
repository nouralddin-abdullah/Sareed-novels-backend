using MediatR;
using System.Text.Json.Serialization;

namespace Application.Users.Commands.FollowUser;

public class FollowUserCommand : IRequest<OperationResult>
{
    public string UserIdToFollow { get; set; } = default!;
}

public class OperationResult
{
    public bool Success { get; set; }

    /// <summary>
    /// A stable reason for clients to branch on (AlreadyFollowing, NovelNotFound, ...): set on every failure, and on a
    /// success only where it tells something apart. Left out of the JSON when null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; set; }

    /// <summary>For people, in Arabic.</summary>
    public string Message { get; set; } = default!;

    /// <summary>
    /// The request field a failure is about, where the code alone doesn't say (update-me's UploadFailed: ProfilePhoto
    /// or ProfileBanner). Left out of the JSON when null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Field { get; set; }

    /// <summary>
    /// The state was already what the request asked for (already liked, not following...), so nothing changed. The
    /// idempotent writes answer these 204 No Content (#25); Code still says which, for callers that read the result.
    /// </summary>
    [JsonIgnore]
    public bool Unchanged { get; init; }

    /// <summary>A result for a request whose state was already as asked (<see cref="Unchanged"/>).</summary>
    public static OperationResult AlreadyDone(string code, string message) =>
        new() { Success = false, Code = code, Message = message, Unchanged = true };
}
