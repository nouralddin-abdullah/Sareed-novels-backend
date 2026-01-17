using Application.Competitions.DTOs;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Competitions.Commands.UpdateCompetition;

public class UpdateCompetitionCommandHandler(
    ICompetitionRepository competitionRepository,
    IMapper mapper) : IRequestHandler<UpdateCompetitionCommand, CompetitionDetailDto>
{
    public async Task<CompetitionDetailDto> Handle(UpdateCompetitionCommand request, CancellationToken cancellationToken)
    {
        var competition = await competitionRepository.GetByIdWithParticipantsAsync(request.Id)
            ?? throw new NotFoundException("Competition not found");

        // Update only provided fields
        if (request.Name != null) competition.Name = request.Name;
        if (request.ImageUrl != null) competition.ImageUrl = request.ImageUrl;
        if (request.TotalPrize.HasValue) competition.TotalPrize = request.TotalPrize.Value;
        if (request.PrizeFirstPlace.HasValue) competition.PrizeFirstPlace = request.PrizeFirstPlace.Value;
        if (request.PrizeSecondPlace.HasValue) competition.PrizeSecondPlace = request.PrizeSecondPlace.Value;
        if (request.PrizeThirdPlace.HasValue) competition.PrizeThirdPlace = request.PrizeThirdPlace.Value;
        if (request.ParticipationStartDate.HasValue) competition.ParticipationStartDate = request.ParticipationStartDate.Value;
        if (request.ParticipationEndDate.HasValue) competition.ParticipationEndDate = request.ParticipationEndDate.Value;
        if (request.JudgmentStartDate.HasValue) competition.JudgmentStartDate = request.JudgmentStartDate.Value;
        if (request.JudgmentEndDate.HasValue) competition.JudgmentEndDate = request.JudgmentEndDate.Value;
        if (request.ResultsDate.HasValue) competition.ResultsDate = request.ResultsDate.Value;
        if (request.MaxNovelAgeDays.HasValue) competition.MaxNovelAgeDays = request.MaxNovelAgeDays.Value;
        if (request.MinChapters.HasValue) competition.MinChapters = request.MinChapters.Value;
        if (request.Status != null) competition.Status = request.Status;
        if (request.IsActive.HasValue) competition.IsActive = request.IsActive.Value;

        await competitionRepository.UpdateAsync(competition);

        return mapper.Map<CompetitionDetailDto>(competition);
    }
}
