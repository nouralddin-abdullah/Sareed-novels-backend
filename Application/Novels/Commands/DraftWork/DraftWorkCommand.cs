using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Novels.Commands.DraftWork;

public class DraftWorkCommand(Guid novelId) : IRequest<OperationResult>
{
    public Guid NovelId { get; set; } = novelId;
}
