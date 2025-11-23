using Application.Gifts.DTOs;
using MediatR;

namespace Application.Gifts.Queries.GetTopSupporters;

public class GetTopSupportersQuery(Guid novelId, int topCount = 10) : IRequest<List<TopSupporterDto>>
{
    public Guid NovelId { get; set; } = novelId;
    public int TopCount { get; set; } = topCount;
}
