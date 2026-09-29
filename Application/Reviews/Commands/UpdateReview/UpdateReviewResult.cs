using Application.Reviews.DTO;
using Application.Users.Commands.FollowUser;

namespace Application.Reviews.Commands.UpdateReview;

/// <summary>The usual success and message, and the review as it is now.</summary>
public class UpdateReviewResult : OperationResult
{
    /// <summary>The review exactly as the novel's review list returns it, so the apps can replace it in place.</summary>
    public ReviewsDTO? Review { get; set; }
}
