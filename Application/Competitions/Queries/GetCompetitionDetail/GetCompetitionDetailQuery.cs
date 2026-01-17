using Application.Competitions.DTOs;
using MediatR;

namespace Application.Competitions.Queries.GetCompetitionDetail;

public class GetCompetitionDetailQuery : IRequest<CompetitionDetailDto>
{
    public Guid? Id { get; set; }
    public string? Slug { get; set; }
}
