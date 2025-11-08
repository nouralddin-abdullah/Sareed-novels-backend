using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Library.Commands.RecalculateSequences;

public record RecalculateChapterSequencesCommand(Guid NovelId) : IRequest<OperationResult>;
