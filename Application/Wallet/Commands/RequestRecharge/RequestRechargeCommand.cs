using Application.Users.Commands.FollowUser;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Wallet.Commands.RequestRecharge;

public class RequestRechargeCommand : IRequest<OperationResult>
{
    public int PointsRequested { get; set; }
    public string PaymentMethod { get; set; } = default!;
    public IFormFile PaymentProof { get; set; } = default!;
}
