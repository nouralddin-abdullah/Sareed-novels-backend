using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Privileges.Commands.ManualUnlock;

/// <summary>Frees one locked chapter of the novel for everyone, for good (#94).</summary>
public class ManualUnlockChapterCommand : IRequest<OperationResult>
{
    public Guid NovelId { get; set; }
    public Guid ChapterId { get; set; }
}
