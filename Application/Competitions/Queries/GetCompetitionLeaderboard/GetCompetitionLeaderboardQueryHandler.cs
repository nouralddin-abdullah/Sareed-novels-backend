using Application.Competitions.DTOs;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Competitions.Queries.GetCompetitionLeaderboard;

public class GetCompetitionLeaderboardQueryHandler(
    ICompetitionRepository competitionRepository,
    ICompetitionParticipantRepository participantRepository,
    IMapper mapper) : IRequestHandler<GetCompetitionLeaderboardQuery, List<CompetitionLeaderboardEntryDto>>
{
    public async Task<List<CompetitionLeaderboardEntryDto>> Handle(GetCompetitionLeaderboardQuery request, CancellationToken cancellationToken)
    {
        if (!await competitionRepository.ExistsAsync(request.CompetitionId))
        {
            throw new NotFoundException("Competition not found");
        }

        var topParticipants = await participantRepository.GetTopParticipantsAsync(request.CompetitionId, request.Top);

        return mapper.Map<List<CompetitionLeaderboardEntryDto>>(topParticipants);
    }
}
