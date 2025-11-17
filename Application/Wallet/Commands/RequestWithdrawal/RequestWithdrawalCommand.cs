using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Wallet.Commands.RequestWithdrawal;

public class RequestWithdrawalCommand : IRequest<OperationResult>
{
    public int PointsRequested { get; set; }
    public string WithdrawalMethod { get; set; } = default!;
    public string PaymentDetails { get; set; } = default!; // Phone/Email for receiving payment
}
