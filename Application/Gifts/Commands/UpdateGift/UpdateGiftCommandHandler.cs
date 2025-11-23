using Application.Services;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Gifts.Commands.UpdateGift;

public class UpdateGiftCommandHandler(
    ILogger<UpdateGiftCommandHandler> logger,
    IGiftRepository giftRepository,
    IFileUploadService fileUploadService) : IRequestHandler<UpdateGiftCommand, bool>
{
    public async Task<bool> Handle(UpdateGiftCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Updating gift: {GiftId}", request.GiftId);

        var gift = await giftRepository.GetGiftById(request.GiftId)
            ?? throw new NotFoundException("Gift not found");

        if (request.Name != null)
            gift.Name = request.Name;

        if (request.Cost.HasValue)
            gift.Cost = request.Cost.Value;

        if (request.IsActive.HasValue)
            gift.IsActive = request.IsActive.Value;

        if (request.Image != null)
        {
            using var imageStream = request.Image.OpenReadStream();
            gift.ImageUrl = await fileUploadService.UploadGiftImageAsync(
                imageStream,
                request.Image.ContentType,
                gift.Id.ToString()
            );
        }

        gift.UpdatedAt = DateTime.UtcNow;

        var result = await giftRepository.UpdateGift(gift);

        logger.LogInformation("Gift updated successfully: {GiftId}", request.GiftId);

        return result;
    }
}
