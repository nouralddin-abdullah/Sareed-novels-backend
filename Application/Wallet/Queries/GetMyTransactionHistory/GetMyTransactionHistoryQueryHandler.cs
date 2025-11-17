using Application.Users;
using Application.Wallet.DTOs;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Wallet.Queries.GetMyTransactionHistory;

public class GetMyTransactionHistoryQueryHandler(
    IUserContext userContext,
    IPointTransactionRepository transactionRepository,
    IMapper mapper) : IRequestHandler<GetMyTransactionHistoryQuery, (IEnumerable<PointTransactionDto>, int)>
{
    public async Task<(IEnumerable<PointTransactionDto>, int)> Handle(GetMyTransactionHistoryQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        
        var (transactions, totalCount) = await transactionRepository.GetUserTransactionsAsync(
            currentUser.Id,
            request.PageNumber,
            request.PageSize
        );
        
        var dtos = mapper.Map<IEnumerable<PointTransactionDto>>(transactions);
        
        return (dtos, totalCount);
    }
}
