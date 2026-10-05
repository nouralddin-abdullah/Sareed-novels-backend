namespace Application.Chapters.Commands.UpdateChapter;

public class UpdateChapterRequest
{
    public string? Title { get; set; }
    public string? Status { get; set; }
    public string? Content { get; set; }

    /// <summary>The chapter's revision the editor's copy was loaded at (#75); optional.</summary>
    public int? BaseRevision { get; set; }
}
