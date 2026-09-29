namespace Domain.Entities;

public class UserNovelProgress
{
    public string UserId { get; set; } = default!;
    public Guid NovelId { get; set; }
    public Guid LastReadChapterId { get; set; }
    public int LastReadChapterNumber { get; set; }
    public DateTime LastReadAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether the novel's new chapters notify the reader, in the app and by push (#33). On for every new entry, and
    /// for entries from before the column existed; the reader mutes one novel with PATCH /api/library/novel/{id}.
    /// </summary>
    public bool NotifyNewChapters { get; set; } = true;

    public User User { get; set; } = default!;
    public Novel Novel { get; set; } = default!;
    public Chapter LastReadChapter { get; set; } = default!;
}
