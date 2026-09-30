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
                throw new NotFoundException("المسابقة غير موجودة", "CompetitionNotFound");
            }

            var competition = (await competitionRepository.GetByIdAsync(request.CompetitionId))!;

            // Winners are chosen once: finalizing again keeps the ones there are.
            var existing = await winnerRepository.GetByCompetitionIdAsync(request.CompetitionId);
            if (!existing.Any())
            {
                // The top 3 participants win. A competition nobody took part in has no winners, and is completed all
                // the same: that is how an admin closes it.
                var topList = (await participantRepository.GetTopParticipantsAsync(request.CompetitionId, 3)).ToList();
                if (topList.Count > 0)
                {
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
                }
            }

            // Finalized means Completed, whatever the dates say (the stored status wins once it is further along), also
            // when it is finalized again after an admin changed the status.
            if (competition.Status != CompetitionStatus.Completed)
            {
                competition.Status = CompetitionStatus.Completed;
                await competitionRepository.UpdateAsync(competition);
            }

            // Reload winners with navigation properties
            return (await winnerRepository.GetByCompetitionIdAsync(request.CompetitionId)).ToList();
        }, cancellationToken);

        return mapper.Map<List<CompetitionWinnerDto>>(winners);
    }
}
