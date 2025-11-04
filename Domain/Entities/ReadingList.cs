namespace Domain.Entities;

public class ReadingList
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = default!;
    public User Owner { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }
    public bool IsPublic { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int NovelsCount { get; set; } = 0;
    public int FollowersCount { get; set; } = 0;
    public ICollection<ReadingListNovel> Novels { get; set; } = new List<ReadingListNovel>();
    public ICollection<ReadingListFollower> Followers { get; set; } = new List<ReadingListFollower>();

    public void IncrementNovelsCount()
    {
        NovelsCount++;
        UpdatedAt = DateTime.UtcNow;
    }

    public void DecrementNovelsCount()
    {
        if (NovelsCount > 0)
        {
            NovelsCount--;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void IncrementFollowersCount()
    {
        FollowersCount++;
    }

    public void DecrementFollowersCount()
    {
        if (FollowersCount > 0)
        {
            FollowersCount--;
        }
    }
}