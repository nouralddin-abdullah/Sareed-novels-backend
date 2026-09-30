using System.Text.Json.Serialization;
using Application.Users.Commands.FollowUser;
using Application.Users.DTOS;

namespace Application.Users.Commands.UpdateMe;

/// <summary>The usual success and message, and the profile as it is after the save (#44).</summary>
public class UpdateMeResult : OperationResult
{
    /// <summary>
    /// The saved profile exactly as GET /api/User/my-profile returns it, written after success and message. Only on a
    /// success: a refusal answers as it did before, without the field.
    /// </summary>
    [JsonPropertyOrder(1)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UserIsProfile? Profile { get; set; }
}
