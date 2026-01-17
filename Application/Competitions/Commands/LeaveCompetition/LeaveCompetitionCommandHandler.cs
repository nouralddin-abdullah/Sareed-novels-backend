using Application.Users;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Competitions.Commands.LeaveCompetition;

public class LeaveCompetitionCommandHandler(
    ICompetitionRepository competitionRepository,
    ICompetitionParticipantRepository participantRepository,
    INovelsRepository novelsRepository,
    IUserContext userContext) : IRequestHandler<LeaveCompetitionCommand, bool>
{
    public async Task<bool> Handle(LeaveCompetitionCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser()
            ?? throw new ForbidException("You must be logged in");

        var competition = await competitionRepository.GetByIdAsync(request.CompetitionId)
            ?? throw new NotFoundException("Competition not found");

        // Can only leave during participation phase
        if (competition.Status != CompetitionStatus.Participation && competition.Status != CompetitionStatus.Upcoming)
        {
            throw new ForbidException("Cannot leave competition after participation phase has ended");
        }

        var novel = await novelsRepository.GetOne(request.NovelId)
            ?? throw new NotFoundException("Novel not found");

        // Verify ownership
        if (novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("You can only remove your own novels from a competition");
        }

        var participant = await participantRepository.GetByCompetitionAndNovelAsync(request.CompetitionId, request.NovelId)
            ?? throw new NotFoundException("Novel is not participating in this competition");

        await participantRepository.DeleteAsync(participant);

        return true;
    }
}
