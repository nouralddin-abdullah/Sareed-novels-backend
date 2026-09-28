using MediatR;

namespace Application.Chapters.Commands.TrackChapterView;

/// <summary>
/// POST /api/novel/{novelId}/chapter/{chapterId}/view: one read of a chapter the app downloaded for offline reading
/// (GET with ?prefetch=true, which doesn't count) and the reader has now opened. True when it counted.
/// </summary>
public record TrackChapterViewCommand(Guid NovelId, Guid ChapterId) : IRequest<bool>;
