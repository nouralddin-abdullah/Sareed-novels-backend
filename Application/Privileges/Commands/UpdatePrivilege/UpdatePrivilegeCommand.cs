using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Privileges.Commands.UpdatePrivilege;

public class UpdatePrivilegeCommand : IRequest<OperationResult>
{
    public Guid NovelId { get; set; }
    public decimal? NewSubscriptionCost { get; set; }
    
    /// <summary>
    /// The website before #94: moves the first locked chapter forward to this published position, freeing the locked
    /// chapters before it; never back.
    /// </summary>
    public int? NewPrivilegeStartSequence { get; set; }

    /// <summary>New days (1-30, #94), for chapters that come out from now and those still locked.</summary>
    public int? EarlyAccessDays { get; set; }

    /// <summary>True: subscribers only from now (#94); false: back to days (the current ones, or 7). Not true with days.</summary>
    public bool? SubscribersOnly { get; set; }
}
