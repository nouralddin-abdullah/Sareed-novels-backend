using Application.Common;
using Application.Competitions.DTOs;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Competitions.Queries.GetCompetitionNovels;

public class GetCompetitionNovelsQueryHandler(
    ICompetitionRepository competitionRepository,
    ICompetitionParticipantRepository participantRepository,
    IMapper mapper) : IRequestHandler<GetCompetitionNovelsQuery, PagedResult<CompetitionParticipantDto>>
{
    public async Task<PagedResult<CompetitionParticipantDto>> Handle(GetCompetitionNovelsQuery request, CancellationToken cancellationToken)
    {
        if (!await competitionRepository.ExistsAsync(request.CompetitionId))
        {
            throw new NotFoundException("Competition not found");
        }

        var totalCount = await participantRepository.GetParticipantCountAsync(request.CompetitionId);

        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        var participants = request.SortBy.ToLower() == "newest"
            ? await participantRepository.GetByCompetitionIdOrderedByNewestAsync(request.CompetitionId, pageNumber, pageSize)
            : await participantRepository.GetByCompetitionIdOrderedByPointsAsync(request.CompetitionId, pageNumber, pageSize);

        var dtos = mapper.Map<List<CompetitionParticipantDto>>(participants);

        return new PagedResult<CompetitionParticipantDto>(dtos, totalCount, pageSize, pageNumber);
    }
}
