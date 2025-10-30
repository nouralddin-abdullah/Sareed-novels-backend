    namespace Domain.Entities;

public class Novel
{
    public Guid Id { get; set; }
    public string AuthorId { get; set; } = default!;
    public User Owner { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string Summary { get; set; } = default!;
    public string CoverImageUrl { get; set; } = default!;
    public string Status { get; set; } = "Ongoing";
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }
    public int TotalViews { get; set; }
    public int ChapterCount { get; set; } = 0;

    // Denormalized Reviews
    public decimal AverageWritingQualityScore { get; set; } = 0;
    public decimal AverageUpdatingStabilityScore { get; set; } = 0;
    public decimal AverageCharacterDevelopmentScore { get; set; } = 0;
    public decimal AverageWorldBuildingScore { get; set; } = 0;
    public decimal TotalAverageScore { get; set; } = 0;
    public int ReviewCount { get; set; } = 0;
    public int ViewsToday { get; set; } = 0; // Quick access to today's views
    public DateTime LastViewUpdate { get; set; } = DateTime.UtcNow; // Track when views were last calculated
    public bool IsDraft { get; set; } = false;
    public bool IsDeleted { get; set; } = false;
    public bool IsEligibleForRanking { get; set; } = true; // Allow excluding novels from ranking
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<NovelGenre> NovelGenres { get; set; } = new List<NovelGenre>();
    public ICollection<Chapter> Chapters { get; set; } = new List<Chapter>();
    public ICollection<Character> Characters { get; set; } = new List<Character>();

    public bool IsPubliclyVisible => !IsDraft && !IsDeleted;
    //Score Review System
    public void RecalculateAverageScores()
    {
        if (Reviews.Count == 0)
        {
            AverageWritingQualityScore = 0;
            AverageUpdatingStabilityScore = 0;
            AverageCharacterDevelopmentScore = 0;
            AverageWorldBuildingScore = 0;
            TotalAverageScore = 0;
            ReviewCount = 0;
            return;
        }

        ReviewCount = Reviews.Count;
        AverageWritingQualityScore = Reviews.Average(r => r.WritingQualityScore);
        AverageUpdatingStabilityScore = Reviews.Average(r => r.UpdatingStabilityScore);
        AverageCharacterDevelopmentScore = Reviews.Average(r => r.CharacterDevelopmentScore);
        AverageWorldBuildingScore = Reviews.Average(r => r.WorldBuildingScore);
        TotalAverageScore = (AverageWritingQualityScore + AverageUpdatingStabilityScore +
                           AverageCharacterDevelopmentScore + AverageWorldBuildingScore) / 4;
    }

    // Method to efficiently update scores when adding a new review
    public void AddReviewToAverages(Review newReview)
    {
        if (ReviewCount == 0)
        {
            // First review
            AverageWritingQualityScore = newReview.WritingQualityScore;
            AverageUpdatingStabilityScore = newReview.UpdatingStabilityScore;
            AverageCharacterDevelopmentScore = newReview.CharacterDevelopmentScore;
            AverageWorldBuildingScore = newReview.WorldBuildingScore;
            ReviewCount = 1;
        }
        else
        {
            // Calculate new averages incrementally
            AverageWritingQualityScore = ((AverageWritingQualityScore * ReviewCount) + newReview.WritingQualityScore) / (ReviewCount + 1);
            AverageUpdatingStabilityScore = ((AverageUpdatingStabilityScore * ReviewCount) + newReview.UpdatingStabilityScore) / (ReviewCount + 1);
            AverageCharacterDevelopmentScore = ((AverageCharacterDevelopmentScore * ReviewCount) + newReview.CharacterDevelopmentScore) / (ReviewCount + 1);
            AverageWorldBuildingScore = ((AverageWorldBuildingScore * ReviewCount) + newReview.WorldBuildingScore) / (ReviewCount + 1);
            ReviewCount++;
        }

        TotalAverageScore = (AverageWritingQualityScore + AverageUpdatingStabilityScore +
                           AverageCharacterDevelopmentScore + AverageWorldBuildingScore) / 4;
    }

    // Method to efficiently update scores when removing a review
    public void RemoveReviewFromAverages(Review removedReview)
    {
        if (ReviewCount <= 1)
        {
            // Last review being removed
            AverageWritingQualityScore = 0;
            AverageUpdatingStabilityScore = 0;
            AverageCharacterDevelopmentScore = 0;
            AverageWorldBuildingScore = 0;
            TotalAverageScore = 0;
            ReviewCount = 0;
        }
        else
        {
            // Remove review from averages
            AverageWritingQualityScore = ((AverageWritingQualityScore * ReviewCount) - removedReview.WritingQualityScore) / (ReviewCount - 1);
            AverageUpdatingStabilityScore = ((AverageUpdatingStabilityScore * ReviewCount) - removedReview.UpdatingStabilityScore) / (ReviewCount - 1);
            AverageCharacterDevelopmentScore = ((AverageCharacterDevelopmentScore * ReviewCount) - removedReview.CharacterDevelopmentScore) / (ReviewCount - 1);
            AverageWorldBuildingScore = ((AverageWorldBuildingScore * ReviewCount) - removedReview.WorldBuildingScore) / (ReviewCount - 1);
            ReviewCount--;

            TotalAverageScore = (AverageWritingQualityScore + AverageUpdatingStabilityScore +
                               AverageCharacterDevelopmentScore + AverageWorldBuildingScore) / 4;
        }
    }
    //Score Review System Ending
}
