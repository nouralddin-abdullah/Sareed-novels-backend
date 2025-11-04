using Domain.Entities;

namespace Domain.Repositories;

public interface IChapterParagraphsRepository
{
    Task<List<ChapterParagraph>> GetChapterParagraphs(Guid chapterId);
    Task<ChapterParagraph?> GetParagraphById(Guid paragraphId);
    Task<bool> CreateParagraphs(List<ChapterParagraph> paragraphs);
    Task<bool> UpdateParagraph(ChapterParagraph paragraph);
    Task<bool> DeleteParagraph(Guid paragraphId);
}
