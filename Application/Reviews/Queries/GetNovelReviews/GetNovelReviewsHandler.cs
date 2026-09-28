using Application.Common;
using Application.Reviews.DTO;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Reviews.Queries.GetNovelReviews;

public class GetNovelReviewsHandler(
    ILogger<GetNovelReviewsHandler> logger, 
    INovelsRepository novelsRepository, 
    IReviewLikesRepository reviewLikesRepository, 
    IUserContext userContext, 
    IMapper mapper, 
    IReviewsRepository reviewsRepository) : IRequestHandler<GetNovelReviewsQuery, NovelReviewsResponse>
{
    public async Task<NovelReviewsResponse> Handle(GetNovelReviewsQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting reviews for novel id: {id}", request.NovelId);
        var novel = await novelsRepository.GetOne(request.NovelId) 
            ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        
        // Reviews by users the viewer blocked are left out. A size of 0 or less used to return every review.
        var currentUser = userContext.GetCurrentUser();
        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        var (reviewsRaw, totalCount) = await reviewsRepository.GetNovelReviews(
            novel.Id, 
            pageSize, 
            pageNumber, 
            request.Sorting,
            currentUser?.Id);
        
        var reviewsDto = await ReviewListDtos.Build(reviewsRaw, mapper, reviewLikesRepository, currentUser);

        // Get current user's review (if authenticated)
        CurrentUserReviewDTO? currentUserReview = null;
        if (currentUser != null)
        {
            var userReview = await reviewsRepository.GetUserReviewForNovel(currentUser.Id, request.NovelId);
            if (userReview != null)
            {
                currentUserReview = mapper.Map<CurrentUserReviewDTO>(userReview);
            }
        }

        var response = new NovelReviewsResponse
        {
            Reviews = reviewsDto,
            CurrentUserReview = currentUserReview,
            TotalCount = totalCount,
            PageSize = pageSize,
            CurrentPage = pageNumber,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };

        return response;
    }
}
