using Application.Competitions.DTOs;
using AutoMapper;
using Domain.Repositories;
using MediatR;

namespace Application.Competitions.Queries.GetCompetitions;

public class GetCompetitionsQueryHandler(
    ICompetitionRepository competitionRepository,
    ICompetitionParticipantRepository participantRepository,
    IMapper mapper) : IRequestHandler<GetCompetitionsQuery, List<CompetitionDto>>
{
    public async Task<List<CompetitionDto>> Handle(GetCompetitionsQuery request, CancellationToken cancellationToken)
    {
        var competitions = string.IsNullOrEmpty(request.Status)
            ? await competitionRepository.GetAllAsync()
            : await competitionRepository.GetByStatusAsync(request.Status);

        var result = new List<CompetitionDto>();

        foreach (var competition in competitions)
        {
            var dto = mapper.Map<CompetitionDto>(competition);
            dto.ParticipantCount = await participantRepository.GetParticipantCountAsync(competition.Id);
            dto.CanJoin = competition.CanJoin();
            result.Add(dto);
        }

        return result;
    }
}
