using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Privileges.Commands.EnablePrivilege;

public class EnablePrivilegeCommand : IRequest<OperationResult>
{
    public Guid NovelId { get; set; }
    public decimal SubscriptionCost { get; set; }
    
    /// <summary>
    /// The published position of the first chapter to lock (11 or after, at most 20 locked); null locks the last
    /// min(20, published - 10).
    /// </summary>
    public int? PrivilegeStartSequence { get; set; }

    /// <summary>How many days a chapter stays locked (1-30, #94); 7 when neither this nor <see cref="SubscribersOnly"/> is sent.</summary>
    public int? EarlyAccessDays { get; set; }

    /// <summary>Chapters stay locked for non-subscribers until the author frees them (#94); not with <see cref="EarlyAccessDays"/>.</summary>
    public bool? SubscribersOnly { get; set; }
}
