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
    IGiftRepository giftRepository,
    IMapper mapper) : IRequestHandler<GetMyTransactionHistoryQuery, (IEnumerable<PointTransactionDto>, int)>
{
    public async Task<(IEnumerable<PointTransactionDto>, int)> Handle(GetMyTransactionHistoryQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        
        var (transactions, totalCount) = await transactionRepository.GetUserTransactionsAsync(
            currentUser.Id,
            pageNumber,
            pageSize,
            TransactionTypeFilter.Parse(request.Types)
        );
        
        var dtos = mapper.Map<List<PointTransactionDto>>(transactions);

        // The novel's current slug and title, and the gift's Arabic name, in one lookup each for the page (a deleted
        // novel has neither; a retired gift keeps its name).
        var novels = await novelsRepository.GetSlugsAndTitlesAsync(dtos.Select(t => t.NovelId).OfType<Guid>().Distinct().ToList());
        var gifts = await giftRepository.GetArabicNamesAsync(dtos.Select(t => t.GiftId).OfType<Guid>().Distinct().ToList());
        foreach (var dto in dtos)
        {
            if (dto.NovelId is { } novelId && novels.TryGetValue(novelId, out var novel))
            {
                (dto.NovelSlug, dto.NovelTitle) = novel;
            }
            dto.GiftNameAr = dto.GiftId is { } giftId ? gifts.GetValueOrDefault(giftId) : null;
        }

        return (dtos, totalCount);
    }
}
