using Application.Chapters.DTOS;
using MediatR;

namespace Application.Chapters.Queries.GetChapterReader;

public class GetChapterReaderQuery(Guid novelId, Guid chapterId) : IRequest<ChapterSingleReaderDTO>
{
    public Guid NovelId { get; set; } = novelId;
    public Guid ChapterId { get; set; } = chapterId;

    /// <summary>
    /// Whether opening the chapter counts as a read (once per visitor per day). False for a download for offline
    /// reading (?prefetch=true or X-Sard-Prefetch: 1), which the app counts with POST .../view when it is read.
    /// </summary>
    public bool TrackView { get; init; } = true;
}
