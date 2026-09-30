using Application.Competitions.DTOs;
using MediatR;

namespace Application.Competitions.Queries.GetCompetitions;

public class GetCompetitionsQuery : IRequest<List<CompetitionDto>>
{
    /// <summary>
    /// Optional filter: competitions whose status now (the dates, or a stored status further along) is this one of
    /// the four, in any letter case; blank lists all. Anything else is 400 InvalidStatus.
    /// </summary>
    public string? Status { get; set; }
}
