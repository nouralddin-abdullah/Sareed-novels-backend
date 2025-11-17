using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Wallet.Commands.RejectRecharge;

public class RejectRechargeCommand : IRequest<OperationResult>
{
    public Guid RequestId { get; set; }
    public string RejectionReason { get; set; } = default!;
}
