using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Wallet.Commands.ApproveRecharge;

public class ApproveRechargeCommand : IRequest<OperationResult>
{
    public Guid RequestId { get; set; }
}
