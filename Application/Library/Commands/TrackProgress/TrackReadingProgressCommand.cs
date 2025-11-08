using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Library.Commands.TrackProgress;

public class TrackReadingProgressCommand(Guid chapterId) : IRequest<OperationResult>
{
    public Guid ChapterId { get; set; } = chapterId;
}
