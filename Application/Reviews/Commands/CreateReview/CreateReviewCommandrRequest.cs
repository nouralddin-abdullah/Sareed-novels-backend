namespace Application.Reviews.Commands.CreateReview;

public class CreateReviewCommandrRequest
{
    public decimal WritingQualityScore { get; set; }
    public decimal UpdatingStabilityScore { get; set; }
    public decimal CharacterDevelopmentScore { get; set; }
    public decimal WorldBuildingScore { get; set; }
    public bool IsSpoiler { get; set; } = false;
    public string? Content { get; set; }
}
