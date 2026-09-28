using Domain.Moderation;

namespace Application.Services;

/// <summary>
/// Deletes a member's account for good: the member's own deletion (DELETE /api/User/me) or an admin's
/// (DELETE /api/admin/users/{userId}), with the same result. The user row stays, anonymized, so the comments, reviews
/// and posts they wrote keep an author («مستخدم محذوف»); everything else about them goes:
/// <list type="bullet">
/// <item>Personal data: email, phone, bio, social links, photo and banner (also from storage), password, external
/// sign-ins and old user names; the user name becomes deleted-{shortId}.</item>
/// <item>Their novels are soft-deleted, like an author deleting them.</item>
/// <item>Private data: library and reading progress, reading lists (with other people's follows of them), follows both
/// ways, notifications to them (and their name and photo on the ones they caused), likes (with the counters),
/// privilege subscriptions, push devices and preferences, blocks both ways.</item>
/// <item>Open reports about them are closed (AccountDeleted); the reports they made stay.</item>
/// <item>Money: the wallet balance is forfeited (set to zero, with a ledger row), pending withdrawals are cancelled;
/// the ledger and Google Play purchases stay.</item>
/// <item>Every session ends at once, and the account can never sign in again.</item>
/// </list>
/// The database part is one transaction. Files are deleted from storage after it commits; a failure there is logged and
/// never fails the deletion.
/// </summary>
public interface IAccountDeletionService
{
    /// <summary>Deletes the account. An account that is already deleted (or doesn't exist) is left as it is.</summary>
    Task<AccountDeletionResult> DeleteAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// An admin deletes the account: the same deletion as <see cref="DeleteAsync(string, CancellationToken)"/>, with a
    /// row in the admin audit log written in its transaction, so the record exists exactly when the account was
    /// deleted (not when there was nothing to delete). Whether this account may be deleted (not an admin's) is the
    /// caller's to check.
    /// </summary>
    Task<AccountDeletionResult> DeleteByAdminAsync(string userId, AdminAccountDeletion byAdmin, CancellationToken cancellationToken = default);
}

/// <summary>The admin deleting an account, and why: what the admin audit log records (ids and the admin's words only).</summary>
/// <param name="Note">The admin's note, at most Domain.Entities.AdminAuditLog.NoteMaxLength characters.</param>
public sealed record AdminAccountDeletion(string AdminId, AccountDeletionReason Reason, string? Note);

/// <summary>What a deletion did, for the logs.</summary>
/// <param name="Deleted">False when there was nothing to delete (no such user, or deleted already).</param>
/// <param name="ForfeitedBalance">The wallet balance given up (negative for a debt that was cleared).</param>
public sealed record AccountDeletionResult(
    bool Deleted,
    int NovelsHidden = 0,
    decimal ForfeitedBalance = 0,
    int WithdrawalsCancelled = 0,
    int ReportsClosed = 0,
    int FilesDeleted = 0,
    int FilesNotDeleted = 0)
{
    public static readonly AccountDeletionResult NothingToDelete = new(false);

    /// <summary>When the account was deleted (UTC, the user's DeletedAt); null when there was nothing to delete.</summary>
    public DateTime? DeletedAt { get; init; }
}
