using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Privileges.Commands.DisablePrivilege;

/// <summary>
/// Turns the novel's early access off (#94): every chapter opens to everyone and new chapters don't lock; its
/// subscriptions stay, for when it is turned on again.
/// </summary>
public record DisablePrivilegeCommand(Guid NovelId) : IRequest<OperationResult>;
