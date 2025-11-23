namespace Domain.Entities;

/// <summary>
/// Cached global leaderboard for top supporters across all novels
/// Recalculated manually by admin
/// </summary>
public class GlobalSupporterLeaderboard
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = default!;
    public User User { get; set; } = default!;
    public decimal TotalPointsGifted { get; set; }
    public int TotalGiftsCount { get; set; }
    public int Rank { get; set; }
    public string Period { get; set; } = default!; // "Weekly" or "AllTime"
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
