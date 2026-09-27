using Application.Users.Commands.FollowUser;
using FluentValidation;
using MediatR;

namespace Application.Users.Commands.BlockUser;

/// <summary>POST /api/User/block: the signed-in user blocks <see cref="UserId"/>. Idempotent.</summary>
public class BlockUserCommand : IRequest<BlockUserResult>
{
    // Nullable, so a missing one gets the validator's Arabic message rather than MVC's English "required" one.
    public string? UserId { get; set; }
}

/// <summary>The usual success and message, the user, and whether the signed-in user now blocks them.</summary>
public class BlockUserResult : OperationResult
{
    public string UserId { get; set; } = default!;
    public bool IsBlocked { get; set; }
}

public class BlockUserCommandValidator : AbstractValidator<BlockUserCommand>
{
    public BlockUserCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty().WithMessage("حدد المستخدم");
    }
}
