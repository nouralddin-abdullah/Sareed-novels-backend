using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Privileges.Commands.ManualUnlock;

public class ManualUnlockChapterCommand : IRequest<OperationResult>
{
    public Guid ChapterId { get; set; }
}
