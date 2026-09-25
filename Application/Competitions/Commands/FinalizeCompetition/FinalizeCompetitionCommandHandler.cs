using Application.Competitions.DTOs;
using Application.Services;
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
    ITransactionManager transactionManager,
    IMapper mapper) : IRequestHandler<FinalizeCompetitionCommand, List<CompetitionWinnerDto>>
{
    public async Task<List<CompetitionWinnerDto>> Handle(FinalizeCompetitionCommand request, CancellationToken cancellationToken)
    {
        var winners = await transactionManager.InTransactionAsync(async () =>
        {
            // Lock the competition first: a concurrent finalize waits here until this one commits and then finds the
            // winners, instead of both seeing none and inserting two sets.
            if (!await competitionRepository.LockForUpdateAsync(request.CompetitionId))
            {
                throw new NotFoundException("Competition not found");
            }

            // Already finalized: return the existing winners
            var existing = (await winnerRepository.GetByCompetitionIdAsync(request.CompetitionId)).ToList();
            if (existing.Count > 0)
            {
                return existing;
            }

            var competition = (await competitionRepository.GetByIdAsync(request.CompetitionId))!;

            // Get top 3 participants
            var topList = (await participantRepository.GetTopParticipantsAsync(request.CompetitionId, 3)).ToList();
            if (topList.Count == 0)
            {
                throw new InvalidOperationException("No participants in competition to finalize");
            }

            var prizes = new[] { competition.PrizeFirstPlace, competition.PrizeSecondPlace, competition.PrizeThirdPlace };
            var created = topList.Select((participant, i) => new CompetitionWinner
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
            }).ToList();

            await winnerRepository.CreateRangeAsync(created);

            // Update competition status to completed
            competition.Status = CompetitionStatus.Completed;
            await competitionRepository.UpdateAsync(competition);

            // Reload winners with navigation properties
            return (await winnerRepository.GetByCompetitionIdAsync(request.CompetitionId)).ToList();
        }, cancellationToken);

        return mapper.Map<List<CompetitionWinnerDto>>(winners);
    }
}
