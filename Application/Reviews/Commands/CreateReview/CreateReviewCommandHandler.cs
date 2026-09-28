using Application.Reviews.Queries;
using Application.Services;
using Application.Users;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Reviews.Commands.CreateReview;

public class CreateReviewCommandHandler(
    ILogger<CreateReviewCommandHandler> logger, 
    INovelsRepository novelsRepository, 
    IUserContext userContext, 
    IMapper mapper, 
    IReviewsRepository reviewsRepository,
    IReviewLikesRepository reviewLikesRepository,
    IServiceProvider serviceProvider) : IRequestHandler<CreateReviewCommand, CreateReviewResult>
{
    public async Task<CreateReviewResult> Handle(CreateReviewCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Creating new review {@review}", request);
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        if (novel.AuthorId == currentUser.Id)
        {
            return new CreateReviewResult
            {
                Success = false,
                Code = "CannotReviewOwnNovel",
                Message = "You cannot review your own novel"
            };
        }
        var existingReview = await reviewsRepository.GetUserReviewForNovel(currentUser.Id, request.NovelId);
        if (existingReview != null)
        {
            return new CreateReviewResult
            {
                Success = false,
                Code = "AlreadyReviewed",
                Message = "You have already reviewed this novel"
            };
        }
        var review = mapper.Map<Review>(request);
        review.CreatedAt = DateTime.UtcNow;
        review.NovelId = novel.Id;
        review.ReviewerId = currentUser.Id;
        review.CalculateAverageScore();
        // Also counts the review for its author and recomputes the novel's review stats with SQL COUNT/AVG.
        var result = await reviewsRepository.CreateOne(review);
        if (!result)
        {
            return new CreateReviewResult
            {
                Success = false,
                Code = "OperationFailed",
                Message = "Failed to create review"
            };
        }

        // Fire-and-forget: Send notification to novel author
        _ = SendReviewNotificationInBackground(novel.AuthorId, currentUser.Id, review.Id, novel.Id);

        // Read back as the novel's review list reads reviews, so the app can put it straight into that list.
        var listed = await reviewsRepository.GetReviewAsListedAsync(review.Id)
            ?? throw new InvalidOperationException($"Review {review.Id} was saved but can't be read back");
        var reviewDtos = await ReviewListDtos.Build([listed], mapper, reviewLikesRepository, currentUser);

        return new CreateReviewResult
        {
            Success = true,
            Message = "Review was created",
            Review = reviewDtos.Single()
        };

    }
    
    private async Task SendReviewNotificationInBackground(string novelAuthorId, string reviewerUserId, Guid reviewId, Guid novelId)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var backgroundUserManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var backgroundNotificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
            var backgroundNovelsRepository = scope.ServiceProvider.GetRequiredService<INovelsRepository>();
            
            var reviewerUser = await backgroundUserManager.FindByIdAsync(reviewerUserId);
            var novel = await backgroundNovelsRepository.GetOne(novelId);
            
            if (reviewerUser != null && novel != null)
            {
                await backgroundNotificationService.SendReviewOnNovelNotification(novelAuthorId, reviewerUser, reviewId, novel);
                logger.LogDebug("Sent ReviewOnNovel notification to user {UserId}", novelAuthorId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send ReviewOnNovel notification in background");
        }
    }
}
