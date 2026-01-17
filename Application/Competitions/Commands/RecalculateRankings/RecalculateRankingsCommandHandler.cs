using Application.Competitions.DTOs;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Competitions.Commands.RecalculateRankings;

public class RecalculateRankingsCommandHandler(
    ICompetitionRepository competitionRepository,
    ICompetitionParticipantRepository participantRepository,
    IMapper mapper) : IRequestHandler<RecalculateRankingsCommand, List<CompetitionLeaderboardEntryDto>>
{
    public async Task<List<CompetitionLeaderboardEntryDto>> Handle(RecalculateRankingsCommand request, CancellationToken cancellationToken)
    {
        var competition = await competitionRepository.GetByIdAsync(request.CompetitionId)
            ?? throw new NotFoundException("Competition not found");

        // Get all participants with novels loaded
        var participants = await participantRepository.GetByCompetitionIdAsync(request.CompetitionId);
        var participantList = participants.ToList();

        if (!participantList.Any())
        {
            return new List<CompetitionLeaderboardEntryDto>();
        }

        // Calculate points for each participant
        foreach (var participant in participantList)
        {
            var competitionViews = participant.Novel.TotalViews - participant.ViewsAtJoin;
            
            // Points calculation: 1 point per 10 views + novel's ranking score
            var viewPoints = competitionViews / 10m;
            var rankingBonus = participant.Novel.TotalAverageScore * 10; // Multiply rating by 10 for weight
            
            participant.CurrentPoints = viewPoints + rankingBonus;
        }

        // Sort by total points (CurrentPoints + ExtraPoints) then by competition views
        var rankedParticipants = participantList
            .OrderByDescending(p => p.CurrentPoints + p.ExtraPoints)
            .ThenByDescending(p => p.Novel.TotalViews - p.ViewsAtJoin)
            .ToList();

        // Assign ranks
        for (int i = 0; i < rankedParticipants.Count; i++)
        {
            rankedParticipants[i].CurrentRank = i + 1;
        }

        // Save updated rankings
        await participantRepository.UpdateRankingsAsync(request.CompetitionId, rankedParticipants);

        // Map to DTOs
        return mapper.Map<List<CompetitionLeaderboardEntryDto>>(rankedParticipants);
    }
}
