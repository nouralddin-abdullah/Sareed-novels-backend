namespace Domain.Entities;

public class NovelViews
{
    public int Id { get; set; }
    public Guid NovelId { get; set; }
    public Novel Novel { get; set; } = default!;
    public DateTime ViewDate { get; set; } // Date only, not timestamp
    public int ViewCount { get; set; } = 0; // Daily view count
}
