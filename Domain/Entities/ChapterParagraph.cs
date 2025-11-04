namespace Domain.Entities;

public class ChapterParagraph
{
    public Guid Id { get; set; }
    public Guid ChapterId { get; set; }
    public Chapter Chapter { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string ContentHash { get; set; } = default!; // SHA256 hash for fast matching
    public int OrderIndex { get; set; }
    public string ContentType { get; set; } = "text";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public int CommentsCount { get; set; } = 0;

    public void IncrementCommentsCount()
    {
        CommentsCount++;
    }

    public void DecrementCommentsCount()
    {
        if (CommentsCount > 0)
        {
            CommentsCount--;
        }
    }
}
