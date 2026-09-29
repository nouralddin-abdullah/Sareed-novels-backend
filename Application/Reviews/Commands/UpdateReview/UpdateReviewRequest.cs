namespace Application.Reviews.Commands.UpdateReview;

/// <summary>
/// The body of <c>PATCH /api/{novelId}/reviews/{reviewId}</c> (#34). Every field is optional: one left out (or null)
/// stays as it is. Sent fields follow the rules of writing a review (<see cref="ReviewRules"/>). Empty
/// <see cref="Content"/> removes the text.
/// </summary>
public class UpdateReviewRequest
{
    public decimal? WritingQualityScore { get; set; }
    public decimal? UpdatingStabilityScore { get; set; }
    public decimal? CharacterDevelopmentScore { get; set; }
    public decimal? WorldBuildingScore { get; set; }
    public string? Content { get; set; }
    public bool? IsSpoiler { get; set; }
}
