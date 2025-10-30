using Domain.Entities;

namespace Domain.Repositories;

public interface IChaptersRepository
{
    Task<bool> CreateChapter(Chapter chapter);
    Task<Chapter?> GetChapterById(Guid chapterId);
    Task<Chapter?> GetChapterBySlug(string slug);
    Task<bool> UpdateChapter(Chapter chapter);
    Task<int> GetNextChapterIndex(Guid novelId);
    Task<bool> DeleteChapter(Chapter chapter);
    Task<IEnumerable<Chapter>> GetChaptersAuthorView(Guid novelId);
    Task<IEnumerable<Chapter>> GetChaptersReaderView(Guid novelId);
    Task<bool> ReorderChapters(Guid novelId, List<Guid> orderedChapterIds);
    Task<string?> GetNextChapterSlug(Guid novelId, int currentChapterIndex);
}
