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
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        
        var wallet = await walletService.GetOrCreateWalletAsync(currentUser.Id);
        
        return mapper.Map<WalletDto>(wallet);
    }
}
