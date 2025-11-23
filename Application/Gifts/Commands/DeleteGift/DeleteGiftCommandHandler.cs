using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Gifts.Commands.DeleteGift;

public class DeleteGiftCommandHandler(
    ILogger<DeleteGiftCommandHandler> logger,
    IGiftRepository giftRepository) : IRequestHandler<DeleteGiftCommand, bool>
{
    public async Task<bool> Handle(DeleteGiftCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Soft deleting gift: {GiftId}", request.GiftId);

        var result = await giftRepository.DeleteGift(request.GiftId);

        if (result)
        {
            logger.LogInformation("Gift soft deleted successfully: {GiftId}", request.GiftId);
        }
        else
        {
            logger.LogWarning("Gift not found or already deleted: {GiftId}", request.GiftId);
        }

        return result;
    }
}
