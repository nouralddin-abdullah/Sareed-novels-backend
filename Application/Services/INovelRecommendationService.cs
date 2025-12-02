using Application.Novels.DTOS;

namespace Application.Services;

public interface INovelRecommendationService
{
    Task<List<NovelRecommendationDto>> GetRecommendationsAsync(Guid novelId, int count = 10);
    Task InvalidateRecommendationCacheAsync(Guid novelId);
}
