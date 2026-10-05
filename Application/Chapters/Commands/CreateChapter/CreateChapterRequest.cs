namespace Application.Chapters.Commands.CreateChapter;

public class CreateChapterRequest
{
    public string Status { get; set; } = default!;
    public string Content { get; set; } = default!;
    public string Title { get; set; } = default!;

    /// <summary>
    /// When the draft publishes itself (#77): a time to come, UTC (with "Z", an offset, or neither, which is UTC). Only
    /// with <see cref="Status"/> Draft. Left out or null: not scheduled.
    /// </summary>
    public DateTime? PublishAt { get; set; }
}
