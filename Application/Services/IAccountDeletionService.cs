namespace Application.Services;

/// <summary>
/// Deletes a member's account for good (DELETE /api/User/me). The user row stays, anonymized, so the comments, reviews
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
}

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
}
