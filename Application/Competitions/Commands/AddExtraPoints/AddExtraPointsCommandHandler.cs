using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Competitions.Commands.AddExtraPoints;

public class AddExtraPointsCommandHandler(
    ICompetitionParticipantRepository participantRepository) : IRequestHandler<AddExtraPointsCommand, bool>
{
    public async Task<bool> Handle(AddExtraPointsCommand request, CancellationToken cancellationToken)
    {
        var participant = await participantRepository.GetByCompetitionAndNovelAsync(request.CompetitionId, request.NovelId)
            ?? throw new NotFoundException("Participant not found in this competition");

        participant.ExtraPoints = request.ExtraPoints;
        await participantRepository.UpdateAsync(participant);

        return true;
    }
}
