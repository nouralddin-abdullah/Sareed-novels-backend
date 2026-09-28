using Application.Gifts.DTOs;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Gifts.Commands.CreateGift;

public class CreateGiftCommand : IRequest<GiftDto>
{
    public string Name { get; set; } = default!;
    /// <summary>The Arabic name; the (English) name when left out.</summary>
    public string? NameAr { get; set; }
    public IFormFile Image { get; set; } = default!;
    public decimal Cost { get; set; }
}
