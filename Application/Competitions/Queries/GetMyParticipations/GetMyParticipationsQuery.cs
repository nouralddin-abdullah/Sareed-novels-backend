using Application.Competitions.DTOs;
using MediatR;

namespace Application.Competitions.Queries.GetMyParticipations;

public class GetMyParticipationsQuery : IRequest<List<MyCompetitionParticipationDto>>
{
}
