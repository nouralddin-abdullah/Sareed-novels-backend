namespace Domain.Library;

/// <summary>A chapter as the library needs it; <see cref="ChapterIndex"/> is the author's order within the novel.</summary>
public sealed record ChapterOutline(Guid Id, string Title, int ChapterIndex);

/// <summary>One novel in a reader's library: the stored "stopped at" row plus what the novel looks like now.</summary>
/// <param name="PublishedChapters">The novel's published chapters in reading order.</param>
/// <param name="NotifyNewChapters">Whether the reader gets the novel's new chapters as notifications (#33).</param>
/// <param name="LastChapterPublishedAt">
/// When the newest of the published chapters came out (UTC): the latest <see cref="Domain.Entities.Chapter.PublishedAt"/>
/// among them, read from the chapters, not stored; null when none is published.
/// </param>
/// <param name="NewChaptersCount">
/// How many of the published chapters came out after <see cref="LastReadAt"/> (#45): those whose
/// <see cref="Domain.Entities.Chapter.PublishedAt"/> is later than it, one out at that very instant not included. Counted
/// over the same chapters as <see cref="LastChapterPublishedAt"/>, so it is above 0 exactly when that is later than
/// <see cref="LastReadAt"/>.
/// </param>
public sealed record LibraryEntry(
    Guid NovelId,
    string Title,
    string Slug,
    string CoverImageUrl,
    decimal TotalAverageScore,
    int TotalViews,
    string AuthorUserName,
    string AuthorDisplayName,
    string? AuthorProfilePhoto,
    ChapterOutline LastReadChapter,
    DateTime LastReadAt,
    IReadOnlyList<ChapterOutline> PublishedChapters,
    bool NotifyNewChapters,
    DateTime? LastChapterPublishedAt,
    int NewChaptersCount);

/// <summary>Where a reader resumes a novel: chapter <see cref="ChapterNumber"/> of the published chapters.</summary>
public sealed record ResumePoint(Guid ChapterId, string ChapterTitle, int ChapterNumber, int PublishedChapters, decimal ProgressPercentage);

public static class ReadingPosition
{
    /// <summary>
    /// Resolves a stored "stopped at" row against the novel as it is now. The chapter id is the source of truth:
    /// chapter numbers are positions among published chapters and shift when chapters are deleted, unpublished or
    /// reordered. If the last-read chapter is no longer published, the reader resumes at the nearest published chapter
    /// before it (or the first one), so "continue reading" never points at a chapter readers can't open. When nothing is
    /// published the stored chapter is kept at number 0. The percentage is always within 0-100.
    /// </summary>
    public static ResumePoint Resolve(ChapterOutline lastRead, IReadOnlyList<ChapterOutline> publishedInOrder)
    {
        var total = publishedInOrder.Count;
        if (total == 0)
        {
            return new ResumePoint(lastRead.Id, lastRead.Title, 0, 0, 0);
        }

        var position = -1;
        for (var i = 0; i < total; i++)
        {
            if (publishedInOrder[i].Id == lastRead.Id)
            {
                position = i;
                break;
            }
        }

        if (position < 0)
        {
            // Nearest published chapter at or before where the reader was; the first chapter if none is before it.
            position = 0;
            for (var i = 0; i < total && publishedInOrder[i].ChapterIndex <= lastRead.ChapterIndex; i++)
            {
                position = i;
            }
        }

        var chapter = publishedInOrder[position];
        var number = position + 1;
        return new ResumePoint(chapter.Id, chapter.Title, number, total, Math.Round((decimal)number / total * 100, 1));
    }
}
