using MediatR;
using System.Text.Json.Serialization;

namespace Application.Gifts.Commands.SendGift;

public class SendGiftCommand : IRequest<OperationResult>
{
    public Guid GiftId { get; set; }
    public Guid NovelId { get; set; }
    public int Count { get; set; } = 1;
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
}
