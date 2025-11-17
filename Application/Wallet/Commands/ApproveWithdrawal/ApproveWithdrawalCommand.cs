using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Wallet.Commands.ApproveWithdrawal;

public class ApproveWithdrawalCommand : IRequest<OperationResult>
{
    public Guid RequestId { get; set; }
}
