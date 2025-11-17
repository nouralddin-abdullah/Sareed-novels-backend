using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Wallet.Commands.RejectWithdrawal;

public class RejectWithdrawalCommand : IRequest<OperationResult>
{
    public Guid RequestId { get; set; }
    public string RejectionReason { get; set; } = default!;
}
