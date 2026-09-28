using Application.Services;
using Application.Users;
using Application.Wallet.DTOs;
using Domain.Exceptions;
using MediatR;

namespace Application.Wallet.Commands.VerifyPlayPurchase;

public class VerifyPlayPurchaseCommandHandler(
    IUserContext userContext,
    IPlayBillingService playBilling) : IRequestHandler<VerifyPlayPurchaseCommand, PlayPurchaseResult>
{
    public Task<PlayPurchaseResult> Handle(VerifyPlayPurchaseCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        return playBilling.VerifyPurchaseAsync(currentUser.Id, request.ProductId, request.PurchaseToken, request.OrderId, cancellationToken);
    }
}
