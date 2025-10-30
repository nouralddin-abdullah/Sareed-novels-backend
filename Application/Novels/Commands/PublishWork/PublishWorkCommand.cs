using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Novels.Commands.PublishWork;

public class PublishWorkCommand(Guid novelId) : IRequest<OperationResult>
{
    public Guid NovelId { get; set; } = novelId;
}
