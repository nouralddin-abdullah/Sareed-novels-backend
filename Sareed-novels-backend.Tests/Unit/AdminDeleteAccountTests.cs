using Application.Services;
using Application.Users;
using Application.Users.Commands.AdminDeleteAccount;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Moderation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sareed_novels_backend.Tests.Integration;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// DELETE /api/admin/users/{userId}: what the body may hold, and what the handler decides before and after it hands
/// the account to IAccountDeletionService.
/// </summary>
public class AdminDeleteAccountTests
{
    private const string AdminId = "admin-1";

    private readonly UserManager<User> users =
        Substitute.For<UserManager<User>>(Substitute.For<IUserStore<User>>(), null, null, null, null, null, null, null, null);
    private readonly IAccountDeletionService deletion = Substitute.For<IAccountDeletionService>();
    private readonly IUserContext userContext = Substitute.For<IUserContext>();
    private readonly ListLogger<AdminDeleteAccountCommandHandler> logger = new();

    public AdminDeleteAccountTests() =>
        userContext.GetCurrentUser().Returns(new CurrentUser(AdminId, "admin@example.test", "admin", "المشرف"));

    private Task<AdminDeleteAccountResult> Delete(string userId, string? reason = "Underage", string? note = null) =>
        new AdminDeleteAccountCommandHandler(logger, userContext, users, deletion)
            .Handle(new AdminDeleteAccountCommand(userId, reason, note), CancellationToken.None);

    private User Member(bool admin = false, DateTime? deletedAt = null)
    {
        var user = Seed.User();
        user.DeletedAt = deletedAt;
        users.FindByIdAsync(user.Id).Returns(user);
        users.IsInRoleAsync(user, UserRoles.Admin).Returns(admin);
        return user;
    }

    // ─── The body ───

    [Theory]
    [InlineData("Underage")]
    [InlineData("PolicyViolation")]
    [InlineData("OwnerRequest")]
    [InlineData("underage")]
    [InlineData(" OwnerRequest ")]
    public void Each_reason_is_accepted_by_name_ignoring_case(string reason)
    {
        var result = new AdminDeleteAccountRequestValidator().Validate(new AdminDeleteAccountRequest { Reason = reason });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Spam")]
    [InlineData("0")]
    [InlineData("2")]
    [InlineData("Underage,OwnerRequest")]
    public void A_missing_unknown_or_numeric_reason_is_refused_in_arabic(string? reason)
    {
        var result = new AdminDeleteAccountRequestValidator().Validate(new AdminDeleteAccountRequest { Reason = reason });

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(AdminDeleteAccountRequest.Reason), error.PropertyName);
        Assert.Equal("سبب الحذف غير صالح: Underage أو PolicyViolation أو OwnerRequest", error.ErrorMessage);
    }

    [Fact]
    public void The_note_is_optional_and_at_most_500_characters()
    {
        var validator = new AdminDeleteAccountRequestValidator();

        Assert.True(validator.Validate(new AdminDeleteAccountRequest { Reason = "PolicyViolation" }).IsValid);
        Assert.True(validator.Validate(new AdminDeleteAccountRequest { Reason = "PolicyViolation", Note = new string('ن', 500) }).IsValid);
        var error = Assert.Single(validator.Validate(new AdminDeleteAccountRequest { Reason = "PolicyViolation", Note = new string('ن', 501) }).Errors);
        Assert.Equal("يجب ألا تتجاوز الملاحظة 500 حرف", error.ErrorMessage);
    }

    // ─── The handler ───

    [Fact]
    public async Task A_member_is_deleted_for_the_reason_given_and_the_admin_id_target_id_and_reason_are_logged()
    {
        var member = Member();
        var deletedAt = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        deletion.DeleteByAdminAsync(member.Id, Arg.Any<AdminAccountDeletion>(), Arg.Any<CancellationToken>())
            .Returns(new AccountDeletionResult(true, NovelsHidden: 2, ForfeitedBalance: 150, WithdrawalsCancelled: 1, ReportsClosed: 3, FilesDeleted: 1)
            {
                DeletedAt = deletedAt
            });

        var result = await Delete(member.Id, "policyviolation", "  نشر محتوى مخالف بعد إيقافين  ");

        await deletion.Received(1).DeleteByAdminAsync(member.Id,
            new AdminAccountDeletion(AdminId, AccountDeletionReason.PolicyViolation, "نشر محتوى مخالف بعد إيقافين"), CancellationToken.None);
        await deletion.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
        Assert.Equal(new AdminDeleteAccountResult(member.Id, "PolicyViolation", deletedAt, 2, 150, 1, 3, 1, 0), result);

        var logged = Assert.Single(logger.Entries, e => e.Level == LogLevel.Information);
        Assert.Equal(AdminId, logged.Values["AdminId"]);
        Assert.Equal(member.Id, logged.Values["UserId"]);
        Assert.Equal(AccountDeletionReason.PolicyViolation, logged.Values["Reason"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_note_is_recorded_as_none(string? note)
    {
        var member = Member();
        deletion.DeleteByAdminAsync(default!, default!).ReturnsForAnyArgs(new AccountDeletionResult(true) { DeletedAt = DateTime.UtcNow });

        await Delete(member.Id, "OwnerRequest", note);

        await deletion.Received(1).DeleteByAdminAsync(member.Id,
            new AdminAccountDeletion(AdminId, AccountDeletionReason.OwnerRequest, null), CancellationToken.None);
    }

    [Fact]
    public async Task An_unknown_user_is_a_404()
    {
        var error = await Assert.ThrowsAsync<NotFoundException>(() => Delete(Guid.NewGuid().ToString()));

        Assert.Equal("UserNotFound", error.Code);
        Assert.Equal("المستخدم غير موجود", error.Message);
        Assert.Empty(deletion.ReceivedCalls());
    }

    [Fact]
    public async Task An_account_deleted_already_is_a_409()
    {
        var member = Member(deletedAt: DateTime.UtcNow.AddDays(-1));

        var error = await Assert.ThrowsAsync<ConflictException>(() => Delete(member.Id));

        Assert.Equal("AlreadyDeleted", error.Code);
        Assert.Equal("هذا الحساب محذوف بالفعل", error.Message);
        Assert.Empty(deletion.ReceivedCalls());
    }

    [Fact]
    public async Task An_account_deleted_meanwhile_is_a_409_too()
    {
        var member = Member();
        deletion.DeleteByAdminAsync(default!, default!).ReturnsForAnyArgs(AccountDeletionResult.NothingToDelete);

        var error = await Assert.ThrowsAsync<ConflictException>(() => Delete(member.Id));

        Assert.Equal("AlreadyDeleted", error.Code);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Information);
    }

    [Fact]
    public async Task An_admins_account_is_refused_the_callers_own_too()
    {
        var otherAdmin = Member(admin: true);
        var self = Member(admin: true);
        userContext.GetCurrentUser().Returns(new CurrentUser(self.Id, "self@example.test", self.UserName!, self.DisplayName));

        foreach (var target in new[] { otherAdmin, self })
        {
            var error = await Assert.ThrowsAsync<ForbidException>(() => Delete(target.Id));

            Assert.Equal("CannotDeleteAdmin", error.Code);
            Assert.Equal("لا يمكن حذف حساب مشرف، أزل صلاحية الإشراف عنه أولاً", error.Message);
        }
        Assert.Empty(deletion.ReceivedCalls());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("7")]
    [InlineData("Spam")]
    public async Task Without_a_reason_nothing_is_deleted(string? reason)
    {
        // What a request without a body gets (the validator answers a body with a bad reason first).
        var member = Member();

        var error = await Assert.ThrowsAsync<BadRequestException>(() => Delete(member.Id, reason));

        Assert.Equal("ValidationFailed", error.Code);
        Assert.Equal("سبب الحذف غير صالح: Underage أو PolicyViolation أو OwnerRequest", error.Message);
        Assert.Empty(deletion.ReceivedCalls());
    }
}
