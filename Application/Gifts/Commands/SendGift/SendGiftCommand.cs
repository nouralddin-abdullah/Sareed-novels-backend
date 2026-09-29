using MediatR;
using System.Text.Json.Serialization;

namespace Application.Gifts.Commands.SendGift;

public class SendGiftCommand : IRequest<OperationResult>
{
    public Guid GiftId { get; set; }
    public Guid NovelId { get; set; }
    public int Count { get; set; } = 1;

    /// <summary>
    /// Optional (#31): a short message to the author, shown publicly under the novel's recent gifts. Trimmed; empty or
    /// whitespace-only is no message. At most AppConfig:Gifts:MessageMaxLength (200) user-perceived characters
    /// (<see cref="GiftMessageRules"/>), else 400 GiftMessageTooLong; 403 Blocked when the author blocked the sender.
    /// Either way nothing is charged.
    /// </summary>
    public string? Message { get; set; }
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
