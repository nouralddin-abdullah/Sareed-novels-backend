using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Gifts.Commands.UpdateGift;

public class UpdateGiftCommand : IRequest<bool>
{
    public Guid GiftId { get; set; }
    public string? Name { get; set; }
    public IFormFile? Image { get; set; }
    public decimal? Cost { get; set; }
    public bool? IsActive { get; set; }
}
