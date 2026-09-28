using Domain.Exceptions;
using Domain.Repositories;

namespace Application.ReadingLists.Commands.AddNovelToList;

/// <summary>Which novels may go on a reading list; adding one to a list and creating a list with one share this.</summary>
internal static class NovelForReadingList
{
    /// <summary>The code of the refusal: the novel is a draft (or deleted).</summary>
    public const string NotAddableCode = "NovelNotPublished";

    /// <summary>
    /// Null when the novel may be added, otherwise why not (it's a draft). An unknown or deleted novel throws
    /// <see cref="NotFoundException"/>.
    /// </summary>
    public static async Task<string?> WhyNotAddable(INovelsRepository novelsRepository, Guid novelId)
    {
        var novel = await novelsRepository.GetOne(novelId)
            ?? throw new NotFoundException("Novel not found", "NovelNotFound");

        return novel.IsPubliclyVisible ? null : "Cannot add deleted or draft novels to reading list";
    }
}
