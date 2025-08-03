using Application.Common;
using Application.Reviews.DTO;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Reviews.Queries.GetNovelReviews;

public class GetNovelReviewsHandler(ILogger<GetNovelReviewsHandler> logger, INovelsRepository novelsRepository, IReviewLikesRepository reviewLikesRepository, IUserContext userContext, IMapper mapper, IReviewsRepository reviewsRepository) : IRequestHandler<GetNovelReviewsQuery, PagedResult<ReviewsDTO>>
{
    public async Task<PagedResult<ReviewsDTO>> Handle(GetNovelReviewsQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting reviews for novel id: {id}", request.NovelId);
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");
        var (reviewsRaw, totalCount) = await reviewsRepository.GetNovelReviews(novel.Id, request.PageSize, request.PageNumber, request.Sorting);
        var reviewsDto = mapper.Map<IEnumerable<ReviewsDTO>>(reviewsRaw).ToList(); ;
        var currentUser = userContext.GetCurrentUser();

        if (currentUser != null && reviewsDto.Count != 0)
        {
            var reviewIds = reviewsDto.Select(r => r.Id);

            var userLikedReviewIds = await reviewLikesRepository.GetUserLikedReviewIds(currentUser.Id, reviewIds);

            foreach (var reviewDto in reviewsDto)
            {
                reviewDto.IsLikedByCurrentUser = userLikedReviewIds.Contains(reviewDto.Id);
            }
        }
        var result = new PagedResult<ReviewsDTO>(reviewsDto, totalCount, request.PageSize, request.PageNumber);
        return result;
    }
}
