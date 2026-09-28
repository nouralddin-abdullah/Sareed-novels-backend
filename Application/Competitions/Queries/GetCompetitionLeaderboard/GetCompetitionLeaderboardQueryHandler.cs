using Application.Competitions.DTOs;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Application.Common;

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
            throw new NotFoundException("Competition not found", "CompetitionNotFound");
        }

        // top is a page size: 1..50, whatever the query string says.
        var (_, top) = Paging.Clamp(1, request.Top);
        var topParticipants = await participantRepository.GetTopParticipantsAsync(request.CompetitionId, top);

        return mapper.Map<List<CompetitionLeaderboardEntryDto>>(topParticipants);
    }
}
