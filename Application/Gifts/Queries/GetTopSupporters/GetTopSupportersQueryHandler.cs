using Application.Gifts.DTOs;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Domain.Entities;

namespace Application.Gifts.Queries.GetTopSupporters;

public class GetTopSupportersQueryHandler(
    IGiftTransactionRepository giftTransactionRepository,
    UserManager<User> userManager) : IRequestHandler<GetTopSupportersQuery, List<TopSupporterDto>>
{
    public async Task<List<TopSupporterDto>> Handle(GetTopSupportersQuery request, CancellationToken cancellationToken)
    {
        var topSupporters = await giftTransactionRepository.GetTopSupportersForNovel(
            request.NovelId,
            request.TopCount
        );

        var supporterDtos = new List<TopSupporterDto>();
        int rank = 1;

        foreach (var (userId, totalPoints, totalGifts) in topSupporters)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user != null)
            {
                supporterDtos.Add(new TopSupporterDto
                {
                    UserId = user.Id,
                    UserName = user.UserName!,
                    DisplayName = user.DisplayName,
                    ProfilePhoto = user.ProfilePhoto!,
                    TotalPointsGifted = totalPoints,
                    TotalGiftsCount = totalGifts,
                    Rank = rank++
                });
            }
        }

        return supporterDtos;
    }
}
