using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Privileges.Commands.EnablePrivilege;

public class EnablePrivilegeCommand : IRequest<OperationResult>
{
    public Guid NovelId { get; set; }
    public decimal SubscriptionCost { get; set; }
    
    /// <summary>
    /// The PublishedChapterSequence from which privilege should start.
    /// Example: If you have 50 published chapters and set this to 31,
    /// chapters 31-50 will be locked (20 chapters).
    /// If null, defaults to locking the last 20 published chapters.
    /// </summary>
    public int? PrivilegeStartSequence { get; set; }
}
