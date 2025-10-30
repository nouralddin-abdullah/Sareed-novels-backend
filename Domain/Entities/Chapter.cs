namespace Domain.Entities;

public class Chapter
{
    public Guid Id { get; set; } = default!;
    public Guid NovelId { get; set; } = default!;
    public Novel Novel { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string Status { get; set; } = "Draft";
    public int ChapterIndex { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
