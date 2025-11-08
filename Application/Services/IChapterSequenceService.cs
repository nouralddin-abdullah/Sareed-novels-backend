namespace Application.Services;

public interface IChapterSequenceService
{
    Task RecalculateSequencesForNovelAsync(Guid novelId);
    Task UpdateReadingProgressForNovelAsync(Guid novelId);
}
