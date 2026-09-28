using Application.Services;
using Application.Users;
using Application.Wallet.DTOs;
using Domain.Exceptions;
using MediatR;

namespace Application.Wallet.Queries.GetPlayProducts;

public class GetPlayProductsQueryHandler(
    IUserContext userContext,
    IPlayBillingService playBilling) : IRequestHandler<GetPlayProductsQuery, PlayProductsDto>
{
    public Task<PlayProductsDto> Handle(GetPlayProductsQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");

        return Task.FromResult(playBilling.GetProducts(currentUser.Id));
    }
}
