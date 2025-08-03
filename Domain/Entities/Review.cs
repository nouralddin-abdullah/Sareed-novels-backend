namespace Domain.Entities;

public class Review
{
    public Guid Id { get; set; } = default!;
    public string ReviewerId { get; set; } = default!;
    public User ReviewOwner { get; set; } = default!;
    public Guid NovelId { get; set; } = default!;
    public Novel ReviewedNovel { get; set; } = default!;
    public decimal WritingQualityScore { get; set; }
    public decimal UpdatingStabilityScore { get; set; }
    public decimal CharacterDevelopmentScore { get; set; }
    public decimal WorldBuildingScore { get; set; }
    public decimal TotalAverageScore { get; set; }
    public string? Content { get; set; }
    public bool IsSpoiler { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int LikeCount { get; set; } = 0;
    public ICollection<ReviewLike> Likes { get; set; } = new List<ReviewLike>();
    public void CalculateAverageScore()
    {
        TotalAverageScore = (WritingQualityScore + UpdatingStabilityScore + CharacterDevelopmentScore + WorldBuildingScore) / 4;
    }

    public void IncrementLikeCount()
    {
        LikeCount++;
    }
    public void DecrementLikeCount()
    {
        if (LikeCount > 0)
        {
            LikeCount--;
        }
    }
}
