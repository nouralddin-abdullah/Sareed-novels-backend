using Application.Competitions.DTOs;
using MediatR;

namespace Application.Competitions.Queries.GetCompetitions;

public class GetCompetitionsQuery : IRequest<List<CompetitionDto>>
{
    public string? Status { get; set; } // Filter by status (optional)
}
