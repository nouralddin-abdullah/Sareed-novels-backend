using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Reviews.Commands.CreateReview;

public class CreateReviewCommand(Guid novelId, decimal writingQualityScore, decimal updatingStabilityScore, decimal characterDevelopmentScore, decimal worldBuildingScore, bool isSpoiler, string content) : IRequest<OperationResult>
{
    public Guid NovelId { get; set; } = novelId;
    public decimal WritingQualityScore { get; set; } = writingQualityScore;
    public decimal UpdatingStabilityScore { get; set; } = updatingStabilityScore;
    public decimal CharacterDevelopmentScore { get; set; } = characterDevelopmentScore;
    public decimal WorldBuildingScore { get; set; } = worldBuildingScore;
    public bool IsSpoiler { get; set; } = isSpoiler;
    public string? Content { get; set; } = content;
}
