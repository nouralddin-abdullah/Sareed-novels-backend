using Application.Novels.DTOS;
using Application.Services;
using MediatR;

namespace Application.Novels.Queries.GetNovelRecommendations;

public class GetNovelRecommendationsQueryHandler(
    INovelRecommendationService recommendationService) 
    : IRequestHandler<GetNovelRecommendationsQuery, List<NovelRecommendationDto>>
{
    public async Task<List<NovelRecommendationDto>> Handle(
        GetNovelRecommendationsQuery request, 
        CancellationToken cancellationToken)
    {
        return await recommendationService.GetRecommendationsAsync(request.NovelId, request.Count);
    }
}
