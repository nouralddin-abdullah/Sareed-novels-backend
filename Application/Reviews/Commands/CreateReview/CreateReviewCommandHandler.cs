using Application.Users;
using Application.Users.Commands.FollowUser;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Reviews.Commands.CreateReview;

public class CreateReviewCommandHandler(ILogger<CreateReviewCommandHandler> logger, INovelsRepository novelsRepository, IUserContext userContext, IMapper mapper, IReviewsRepository reviewsRepository) : IRequestHandler<CreateReviewCommand, OperationResult>
{
    public async Task<OperationResult> Handle(CreateReviewCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Creating new review {@review}", request);
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");
        if (novel.AuthorId == currentUser.Id)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You cannot review your own novel"
            };
        }
        var existingReview = await reviewsRepository.GetUserReviewForNovel(currentUser.Id, request.NovelId);
        if (existingReview != null)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You have already reviewed this novel"
            };
        }
        var review = mapper.Map<Review>(request);
        review.Id = Guid.NewGuid();
        review.ReviewerId = currentUser.Id;
        review.CalculateAverageScore();
        var result = await reviewsRepository.CreateOne(review);
        if (!result)
        {
            return new OperationResult
            {
                Success = false,
                Message = "Failed to create review"
            };
        }
        novel.AddReviewToAverages(review);
        await novelsRepository.UpdateOne(novel);
        return new OperationResult
        {
            Success = true,
            Message = "Review was created"
        };

    }
}
