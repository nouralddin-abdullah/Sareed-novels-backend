using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Privileges.Commands.UpdatePrivilege;

public class UpdatePrivilegeCommand : IRequest<OperationResult>
{
    public Guid NovelId { get; set; }
    public decimal? NewSubscriptionCost { get; set; }
    
    /// <summary>
    /// Optional: Move privilege start forward (unlock more chapters).
    /// Can only move FORWARD, not backward (to avoid re-locking chapters).
    /// Example: Move from sequence 31 to 40 = unlock chapters 31-39.
    /// </summary>
    public int? NewPrivilegeStartSequence { get; set; }
}
