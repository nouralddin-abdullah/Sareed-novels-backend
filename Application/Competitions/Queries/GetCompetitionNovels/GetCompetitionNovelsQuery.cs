using Application.Common;
using Application.Competitions.DTOs;
using MediatR;

namespace Application.Competitions.Queries.GetCompetitionNovels;

public class GetCompetitionNovelsQuery : IRequest<PagedResult<CompetitionParticipantDto>>
{
    public Guid CompetitionId { get; set; }
    public string SortBy { get; set; } = "top"; // "top" or "newest"
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
