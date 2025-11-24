using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Privileges.Commands.CancelSubscription;

public class CancelSubscriptionCommand : IRequest<OperationResult>
{
    public Guid NovelId { get; set; }
}
