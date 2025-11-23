using Application.Gifts.DTOs;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Gifts.Commands.CreateGift;

public class CreateGiftCommand : IRequest<GiftDto>
{
    public string Name { get; set; } = default!;
    public IFormFile Image { get; set; } = default!;
    public decimal Cost { get; set; }
}
