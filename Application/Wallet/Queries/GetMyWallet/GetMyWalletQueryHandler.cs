using Application.Services;
using Application.Users;
using Application.Wallet.DTOs;
using AutoMapper;
using Domain.Exceptions;
using MediatR;

namespace Application.Wallet.Queries.GetMyWallet;

public class GetMyWalletQueryHandler(
    IUserContext userContext,
    IWalletService walletService,
    IMapper mapper) : IRequestHandler<GetMyWalletQuery, WalletDto>
{
    public async Task<WalletDto> Handle(GetMyWalletQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        var wallet = await walletService.GetOrCreateWalletAsync(currentUser.Id);
        var withdrawable = await walletService.GetWithdrawableAsync(currentUser.Id);

        var dto = mapper.Map<WalletDto>(wallet);
        dto.Withdrawable = withdrawable.Withdrawable;
        // The earnings figures GET /api/wallet/earnings shows too, from the same call (#78).
        dto.TotalEarned = withdrawable.TotalEarned;
        dto.PendingEarnings = withdrawable.PendingEarnings;
        dto.NextReleaseAt = withdrawable.NextReleaseAt;
        return dto;
    }
}
