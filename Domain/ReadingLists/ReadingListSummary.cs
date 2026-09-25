using Domain.Entities;

namespace Domain.ReadingLists;

/// <summary>A novel shown on a reading list card.</summary>
public sealed record NovelPreview(Guid NovelId, string Slug, string CoverImageUrl, string Title);

/// <summary>
/// A reading list for list pages: <see cref="VisibleNovelsCount"/> counts only novels readers can open (not draft or
/// deleted), and <see cref="PreviewNovels"/> holds the first few of them in list order.
/// </summary>
public sealed record ReadingListSummary(ReadingList List, int VisibleNovelsCount, IReadOnlyList<NovelPreview> PreviewNovels);
