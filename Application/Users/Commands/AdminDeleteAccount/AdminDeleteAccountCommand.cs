using Application.Services;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Moderation;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.AdminDeleteAccount;

/// <summary>The body of DELETE /api/admin/users/{userId}.</summary>
public class AdminDeleteAccountRequest
{
    /// <summary>Underage | PolicyViolation | OwnerRequest (nullable, so a missing one gets the validator's Arabic message).</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Optional, at most 500 characters: the admin's own words, kept in the audit log, so never the member's personal data.
    /// </summary>
    public string? Note { get; set; }
}

public class AdminDeleteAccountRequestValidator : AbstractValidator<AdminDeleteAccountRequest>
{
    public const string InvalidReason = "سبب الحذف غير صالح: Underage أو PolicyViolation أو OwnerRequest";

    public AdminDeleteAccountRequestValidator()
    {
        // By name only: numbers ("0") are refused.
        RuleFor(r => r.Reason)
            .Must(reason => EnumNames.TryParse<AccountDeletionReason>(reason, out _))
            .WithMessage(InvalidReason);

        RuleFor(r => r.Note)
            .MaximumLength(AdminAuditLog.NoteMaxLength)
            .WithMessage($"يجب ألا تتجاوز الملاحظة {AdminAuditLog.NoteMaxLength} حرف");
    }
}

/// <summary>
/// An admin deletes a member's account for good, with exactly the result of the member's own deletion
/// (<see cref="IAccountDeletionService"/>): for a member under 13 (Underage), to enforce the rules (PolicyViolation), or
/// for a member who asked by email (OwnerRequest, once the admin has checked it's them). There is no re-authentication;
/// the admin audit log records who deleted it and why. An admin's account, the caller's own included, is refused.
/// </summary>
public class AdminDeleteAccountCommand(string userId, string? reason, string? note) : IRequest<AdminDeleteAccountResult>
{
    public string UserId { get; } = userId;
    /// <summary>An <see cref="AccountDeletionReason"/> name; null when the request had no body.</summary>
    public string? Reason { get; } = reason;
    public string? Note { get; } = note;
}

/// <summary>What an admin's deletion of an account did.</summary>
/// <param name="DeletedAt">When (UTC), as the account's DeletedAt and the audit row record it.</param>
/// <param name="ForfeitedBalance">The wallet balance given up (negative for a debt that was cleared).</param>
/// <param name="FilesNotDeleted">Their images that could not be deleted from storage (logged as errors); 0 normally.</param>
public record AdminDeleteAccountResult(
    string UserId,
    string Reason,
    DateTime DeletedAt,
    int NovelsHidden,
    decimal ForfeitedBalance,
    int WithdrawalsCancelled,
    int ReportsClosed,
    int FilesDeleted,
    int FilesNotDeleted);

public class AdminDeleteAccountCommandHandler(
    ILogger<AdminDeleteAccountCommandHandler> logger,
    IUserContext userContext,
    UserManager<User> userManager,
    IAccountDeletionService accountDeletion) : IRequestHandler<AdminDeleteAccountCommand, AdminDeleteAccountResult>
{
    public const string UserNotFound = "UserNotFound";
    public const string AlreadyDeleted = "AlreadyDeleted";
    public const string CannotDeleteAdmin = "CannotDeleteAdmin";

    /// <summary>The code of every refused request body (the API's ValidationProblems.Code).</summary>
    public const string ValidationFailed = "ValidationFailed";

    public async Task<AdminDeleteAccountResult> Handle(AdminDeleteAccountCommand request, CancellationToken cancellationToken)
    {
        var admin = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        if (!EnumNames.TryParse<AccountDeletionReason>(request.Reason, out var reason))
        {
            // A body with a bad reason is refused by AdminDeleteAccountRequestValidator before this; here, no body at all.
            throw new BadRequestException(AdminDeleteAccountRequestValidator.InvalidReason, ValidationFailed);
        }

        var user = await userManager.FindByIdAsync(request.UserId)
            ?? throw new NotFoundException("المستخدم غير موجود", UserNotFound);
        if (user.DeletedAt != null)
        {
            throw AccountAlreadyDeleted();
        }

        // An admin's account holds the site's moderation: its admin role is removed first, by the team.
        if (await userManager.IsInRoleAsync(user, UserRoles.Admin))
        {
            logger.LogWarning("Admin {AdminId} tried to delete the account of admin {UserId}: refused", admin.Id, user.Id);
            throw new ForbidException("لا يمكن حذف حساب مشرف، أزل صلاحية الإشراف عنه أولاً", CannotDeleteAdmin);
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        // Once sent, the deletion runs to the end even if the admin's request goes away meanwhile, like the member's own.
        var result = await accountDeletion.DeleteByAdminAsync(
            user.Id, new AdminAccountDeletion(admin.Id, reason, note), CancellationToken.None);
        if (!result.Deleted)
        {
            // Deleted since it was read above, by the member or another admin.
            throw AccountAlreadyDeleted();
        }

        logger.LogInformation(
            "Admin {AdminId} deleted the account of user {UserId} ({Reason}): {NovelsHidden} novels hidden, {Balance} points forfeited, {Withdrawals} withdrawals cancelled, {Reports} reports closed, {Files} files deleted ({FilesFailed} not)",
            admin.Id, user.Id, reason, result.NovelsHidden, result.ForfeitedBalance, result.WithdrawalsCancelled, result.ReportsClosed,
            result.FilesDeleted, result.FilesNotDeleted);

        return new AdminDeleteAccountResult(
            user.Id, reason.ToString(), result.DeletedAt!.Value, result.NovelsHidden, result.ForfeitedBalance,
            result.WithdrawalsCancelled, result.ReportsClosed, result.FilesDeleted, result.FilesNotDeleted);
    }

    private static ConflictException AccountAlreadyDeleted() => new("هذا الحساب محذوف بالفعل", AlreadyDeleted);
}
