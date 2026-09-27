using Application.Wallet.DTOs;
using MediatR;

namespace Application.Wallet.Commands.VerifyPlayPurchase;

/// <summary>A Google Play purchase the app asks the server to verify and credit. The user comes from the token, never the body.</summary>
public class VerifyPlayPurchaseCommand : IRequest<PlayPurchaseResult>
{
    public string? ProductId { get; set; }
    public string? PurchaseToken { get; set; }

    /// <summary>Optional and informational: the order id Google reports is the one recorded.</summary>
    public string? OrderId { get; set; }
}
