namespace Application.Services;

public interface IViewTrackingService
{
    /// <summary>Counts a novel page view at most once per visitor per UTC day.</summary>
    Task TrackNovelView(Guid novelId, string visitorKey, CancellationToken cancellationToken = default);

    /// <summary>Counts a chapter read at most once per visitor per UTC day.</summary>
    Task TrackChapterView(Guid chapterId, Guid novelId, string visitorKey, CancellationToken cancellationToken = default);

    /// <summary>Deletes per-visitor rows older than <paramref name="olderThan"/>; daily totals are kept.</summary>
    Task<int> PruneUniqueViewsAsync(DateTime olderThan, CancellationToken cancellationToken = default);
}
