namespace Application.Chapters.Commands.CreateChapter;

public class CreateChapterRequest
{
    public string Status { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string Title { get; set; } = default!;
}
