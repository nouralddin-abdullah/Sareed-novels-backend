using Application.Competitions.DTOs;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Competitions.Commands.FinalizeCompetition;

public class FinalizeCompetitionCommandHandler(
    ICompetitionRepository competitionRepository,
    ICompetitionParticipantRepository participantRepository,
    ICompetitionWinnerRepository winnerRepository,
    IMapper mapper) : IRequestHandler<FinalizeCompetitionCommand, List<CompetitionWinnerDto>>
{
    public async Task<List<CompetitionWinnerDto>> Handle(FinalizeCompetitionCommand request, CancellationToken cancellationToken)
    {
        var competition = await competitionRepository.GetByIdWithParticipantsAsync(request.CompetitionId)
            ?? throw new NotFoundException("Competition not found");

        // Check if already finalized
        if (competition.Winners.Any())
        {
            // Return existing winners
            return mapper.Map<List<CompetitionWinnerDto>>(competition.Winners.OrderBy(w => w.Rank));
        }

        // Get top 3 participants
        var topParticipants = await participantRepository.GetTopParticipantsAsync(request.CompetitionId, 3);
        var topList = topParticipants.ToList();

        if (!topList.Any())
        {
            throw new InvalidOperationException("No participants in competition to finalize");
        }

        var winners = new List<CompetitionWinner>();
        var prizes = new[] { competition.PrizeFirstPlace, competition.PrizeSecondPlace, competition.PrizeThirdPlace };

        for (int i = 0; i < topList.Count && i < 3; i++)
        {
            var participant = topList[i];
            var winner = new CompetitionWinner
            {
                Id = Guid.NewGuid(),
                CompetitionId = request.CompetitionId,
                NovelId = participant.NovelId,
                AuthorId = participant.Novel.AuthorId,
                Rank = i + 1,
                FinalPoints = participant.CurrentPoints + participant.ExtraPoints,
                FinalViews = participant.Novel.TotalViews - participant.ViewsAtJoin,
                PrizeWon = prizes[i],
                AwardedAt = DateTime.UtcNow
            };
            winners.Add(winner);
        }

        await winnerRepository.CreateRangeAsync(winners);

        // Update competition status to completed
        competition.Status = CompetitionStatus.Completed;
        await competitionRepository.UpdateAsync(competition);

        // Reload winners with navigation properties
        var savedWinners = await winnerRepository.GetByCompetitionIdAsync(request.CompetitionId);
        return mapper.Map<List<CompetitionWinnerDto>>(savedWinners);
    }
}
