namespace Application.Chapters.Commands.ReorderChapter;

public class ReorderChaptersRequest
{
    public List<Guid> OrderedChapterIds { get; set; } = new();
}
