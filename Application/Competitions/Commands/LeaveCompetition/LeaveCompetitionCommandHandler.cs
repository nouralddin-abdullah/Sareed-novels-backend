using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Competitions.Commands.LeaveCompetition;

public class LeaveCompetitionCommandHandler(
    ICompetitionRepository competitionRepository,
    ICompetitionParticipantRepository participantRepository,
    INovelsRepository novelsRepository,
    IUserContext userContext,
    TimeProvider time) : IRequestHandler<LeaveCompetitionCommand, bool>
{
    public async Task<bool> Handle(LeaveCompetitionCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser()
            ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        var competition = await competitionRepository.GetByIdAsync(request.CompetitionId)
            ?? throw new NotFoundException("المسابقة غير موجودة", "CompetitionNotFound");

        // Only until participation ends, by the same rule as joining (CompetitionSchedule).
        if (!competition.CanLeave(time.GetUtcNow().UtcDateTime))
        {
            throw new ForbidException("انتهت فترة المشاركة، فلا يمكن سحب الرواية من المسابقة", "ParticipationEnded");
        }

        var novel = await novelsRepository.GetOne(request.NovelId)
            ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");

        // Verify ownership
        if (novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("يمكنك سحب رواياتك فقط من المسابقة", "NotOwner");
        }

        var participant = await participantRepository.GetByCompetitionAndNovelAsync(request.CompetitionId, request.NovelId)
            ?? throw new NotFoundException("هذه الرواية غير مشاركة في المسابقة", "NotParticipating");

        await participantRepository.DeleteAsync(participant);

        return true;
    }
}
