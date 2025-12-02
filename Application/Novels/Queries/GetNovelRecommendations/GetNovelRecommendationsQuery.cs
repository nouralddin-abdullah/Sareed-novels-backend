using Application.Novels.DTOS;
using MediatR;

namespace Application.Novels.Queries.GetNovelRecommendations;

public class GetNovelRecommendationsQuery : IRequest<List<NovelRecommendationDto>>
{
    public Guid NovelId { get; set; }
    public int Count { get; set; } = 10;
}
