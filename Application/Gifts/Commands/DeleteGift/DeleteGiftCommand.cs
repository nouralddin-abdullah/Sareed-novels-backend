using MediatR;

namespace Application.Gifts.Commands.DeleteGift;

public class DeleteGiftCommand(Guid giftId) : IRequest<bool>
{
    public Guid GiftId { get; set; } = giftId;
}
