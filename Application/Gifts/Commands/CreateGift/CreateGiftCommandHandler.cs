using Application.Gifts.DTOs;
using Application.Services;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Gifts.Commands.CreateGift;

public class CreateGiftCommandHandler(
    ILogger<CreateGiftCommandHandler> logger,
    IGiftRepository giftRepository,
    IFileUploadService fileUploadService,
    IMapper mapper) : IRequestHandler<CreateGiftCommand, GiftDto>
{
    public async Task<GiftDto> Handle(CreateGiftCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Creating new gift: {GiftName}", request.Name);

        var gift = new Gift
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Cost = request.Cost,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Upload image to Cloudflare R2
        using var imageStream = request.Image.OpenReadStream();
        gift.ImageUrl = await fileUploadService.UploadGiftImageAsync(
            imageStream,
            request.Image.ContentType,
            gift.Id.ToString()
        );

        var createdGift = await giftRepository.CreateGift(gift);

        logger.LogInformation("Gift created successfully: {GiftId}", createdGift.Id);

        return mapper.Map<GiftDto>(createdGift);
    }
}
