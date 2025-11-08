namespace Domain.Entities;

public class UserNovelProgress
{
    public string UserId { get; set; } = default!;
    public Guid NovelId { get; set; }
    public Guid LastReadChapterId { get; set; }
    public int LastReadChapterNumber { get; set; }
    public DateTime LastReadAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = default!;
    public Novel Novel { get; set; } = default!;
    public Chapter LastReadChapter { get; set; } = default!;
}
