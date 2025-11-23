using Application.Common;
using Application.Gifts.DTOs;
using MediatR;

namespace Application.Gifts.Queries.GetGlobalLeaderboard;

public class GetGlobalLeaderboardQuery(string period, int pageNumber, int pageSize) : IRequest<GlobalLeaderboardDto>
{
    public string Period { get; set; } = period; // "Weekly" or "AllTime"
    public int PageNumber { get; set; } = pageNumber;
    public int PageSize { get; set; } = pageSize;
}
