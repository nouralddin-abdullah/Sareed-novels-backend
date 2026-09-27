using Application.Wallet.DTOs;
using MediatR;

namespace Application.Wallet.Queries.GetPlayProducts;

public class GetPlayProductsQuery : IRequest<PlayProductsDto>
{
}
