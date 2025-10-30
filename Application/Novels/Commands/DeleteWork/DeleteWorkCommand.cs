using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Novels.Commands.DeleteWork;

public class DeleteWorkCommand(Guid novelId) : IRequest<OperationResult>
{
    public Guid NovelId { get; set; } = novelId;
}
