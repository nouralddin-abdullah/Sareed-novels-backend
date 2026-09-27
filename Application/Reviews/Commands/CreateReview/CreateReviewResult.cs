using Application.Reviews.DTO;
using Application.Users.Commands.FollowUser;

namespace Application.Reviews.Commands.CreateReview;

/// <summary>The usual success and message, and the review that was created.</summary>
public class CreateReviewResult : OperationResult
{
    /// <summary>The new review exactly as the novel's review list returns it; null when nothing was created.</summary>
    public ReviewsDTO? Review { get; set; }
}
