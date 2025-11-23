using MediatR;

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
    public string Message { get; set; } = default!;
}
