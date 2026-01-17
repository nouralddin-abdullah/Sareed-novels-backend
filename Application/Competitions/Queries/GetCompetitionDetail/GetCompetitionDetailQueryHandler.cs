using Application.Competitions.DTOs;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Competitions.Queries.GetCompetitionDetail;

public class GetCompetitionDetailQueryHandler(
    ICompetitionRepository competitionRepository,
    ICompetitionParticipantRepository participantRepository,
    IMapper mapper) : IRequestHandler<GetCompetitionDetailQuery, CompetitionDetailDto>
{
    public async Task<CompetitionDetailDto> Handle(GetCompetitionDetailQuery request, CancellationToken cancellationToken)
    {
        var competition = request.Id.HasValue
            ? await competitionRepository.GetByIdWithParticipantsAsync(request.Id.Value)
            : !string.IsNullOrEmpty(request.Slug)
                ? await competitionRepository.GetBySlugAsync(request.Slug)
                : null;

        if (competition == null)
        {
            throw new NotFoundException("Competition not found");
        }

        // If loaded by slug, we need to load with participants
        if (!request.Id.HasValue && request.Slug != null)
        {
            competition = await competitionRepository.GetByIdWithParticipantsAsync(competition.Id);
        }

        var dto = mapper.Map<CompetitionDetailDto>(competition);
        dto.ParticipantCount = await participantRepository.GetParticipantCountAsync(competition!.Id);

        return dto;
    }
}
