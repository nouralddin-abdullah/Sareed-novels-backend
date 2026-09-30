using Application.Competitions.DTOs;
using AutoMapper;
using Domain.Repositories;
using MediatR;

namespace Application.Competitions.Queries.GetCompetitions;

public class GetCompetitionsQueryHandler(
    ICompetitionRepository competitionRepository,
    ICompetitionParticipantRepository participantRepository,
    IMapper mapper,
    TimeProvider time) : IRequestHandler<GetCompetitionsQuery, List<CompetitionDto>>
{
    public async Task<List<CompetitionDto>> Handle(GetCompetitionsQuery request, CancellationToken cancellationToken)
    {
        // One time for the whole answer, so the filter and the statuses it returns agree.
        var now = time.GetUtcNow().UtcDateTime;
        var competitions = string.IsNullOrWhiteSpace(request.Status)
            ? await competitionRepository.GetAllAsync()
            : await competitionRepository.GetByStatusAsync(CompetitionRules.ParseStatus(request.Status), now);

        var result = new List<CompetitionDto>();

        foreach (var competition in competitions)
        {
            var dto = mapper.MapAt<CompetitionDto>(competition, now);
            dto.ParticipantCount = await participantRepository.GetParticipantCountAsync(competition.Id);
            result.Add(dto);
        }

        return result;
    }
}
