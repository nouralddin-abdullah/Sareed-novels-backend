using Application.Competitions.DTOs;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Competitions.Queries.GetMyParticipations;

public class GetMyParticipationsQueryHandler(
    ICompetitionParticipantRepository participantRepository,
    IUserContext userContext,
    IMapper mapper) : IRequestHandler<GetMyParticipationsQuery, List<MyCompetitionParticipationDto>>
{
    public async Task<List<MyCompetitionParticipationDto>> Handle(GetMyParticipationsQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser()
            ?? throw new ForbidException("You must be logged in");

        var participations = await participantRepository.GetByAuthorIdAsync(currentUser.Id);

        return mapper.Map<List<MyCompetitionParticipationDto>>(participations);
    }
}
