using Application.Users;
using Application.Wallet.DTOs;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Application.Common;

namespace Application.Wallet.Queries.GetMyTransactionHistory;

public class GetMyTransactionHistoryQueryHandler(
    IUserContext userContext,
    IPointTransactionRepository transactionRepository,
    INovelsRepository novelsRepository,
    IMapper mapper) : IRequestHandler<GetMyTransactionHistoryQuery, (IEnumerable<PointTransactionDto>, int)>
{
    public async Task<(IEnumerable<PointTransactionDto>, int)> Handle(GetMyTransactionHistoryQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        
        var (transactions, totalCount) = await transactionRepository.GetUserTransactionsAsync(
            currentUser.Id,
            pageNumber,
            pageSize
        );
        
        var dtos = mapper.Map<List<PointTransactionDto>>(transactions);

        // The novel's current slug and title, in one lookup for the page (a deleted novel has neither).
        var novels = await novelsRepository.GetSlugsAndTitlesAsync(dtos.Select(t => t.NovelId).OfType<Guid>().Distinct().ToList());
        foreach (var dto in dtos)
        {
            if (dto.NovelId is { } novelId && novels.TryGetValue(novelId, out var novel))
            {
                (dto.NovelSlug, dto.NovelTitle) = novel;
            }
        }

        return (dtos, totalCount);
    }
}
