using Application.Wallet.DTOs;
using MediatR;

namespace Application.Wallet.Queries.GetMyWallet;

public class GetMyWalletQuery : IRequest<WalletDto>
{
}
