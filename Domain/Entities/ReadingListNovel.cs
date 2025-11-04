namespace Domain.Entities;

public class ReadingListNovel
{
    public Guid ReadingListId { get; set; }
    public ReadingList ReadingList { get; set; } = default!;

    public Guid NovelId { get; set; }
    public Novel Novel { get; set; } = default!;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public int OrderIndex { get; set; } = 0;
}