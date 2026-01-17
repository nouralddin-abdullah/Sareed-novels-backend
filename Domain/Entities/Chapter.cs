namespace Domain.Entities;

public class Chapter
{
    public Guid Id { get; set; } = default!;
    public Guid NovelId { get; set; } = default!;
    public Novel Novel { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? Content { get; set; } // Made nullable for migration
    public string Status { get; set; } = "Draft";
    public int ChapterIndex { get; set; }
    public int? PublishedChapterSequence { get; set; } // NEW: For efficient querying
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // NEW: Paragraphs
    public ICollection<ChapterParagraph> Paragraphs { get; set; } = new List<ChapterParagraph>();
    
    // Updated comment counts
    public int CommentsCount { get; set; } = 0; // Chapter-level only
    public int TotalCommentsCount { get; set; } = 0; // Chapter + paragraphs
    public int ParagraphsCount { get; set; } = 0;
    public int ViewsCount { get; set; } = 0;

    public void IncrementCommentsCount()
    {
        CommentsCount++;
        TotalCommentsCount++;
    }

    public void DecrementCommentsCount()
    {
        if (CommentsCount > 0)
        {
            CommentsCount--;
        }
        if (TotalCommentsCount > 0)
        {
            TotalCommentsCount--;
        }
    }
    
    public void IncrementTotalCommentsCount()
    {
        TotalCommentsCount++;
    }
    
    public void DecrementTotalCommentsCount()
    {
        if (TotalCommentsCount > 0)
        {
            TotalCommentsCount--;
        }
    }

    public void IncrementViewsCount()
    {
        ViewsCount++;
    }
}
