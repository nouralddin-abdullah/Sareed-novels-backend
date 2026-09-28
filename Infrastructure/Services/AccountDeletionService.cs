using System.Data;
using System.Security.Cryptography;
using Application.Services;
using Domain.Constants;
using Domain.Entities;
using Domain.Moderation;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <inheritdoc />
internal sealed class AccountDeletionService(
    ApplicationDbContext db,
    ILookupNormalizer normalizer,
    ITokenRevocationService tokenRevocation,
    TokenCutoffCache tokenCutoffs,
    IObjectStorage storage,
    TimeProvider time,
    ILogger<AccountDeletionService> logger) : IAccountDeletionService
{
    /// <summary>The ledger row of a forfeited balance, in the wallet history.</summary>
    internal const string ForfeitDescription = "أُلغي الرصيد المتبقي لحذف الحساب";

    public async Task<AccountDeletionResult> DeleteAsync(string userId, CancellationToken cancellationToken = default)
    {
        var deletion = await DeleteDataAsync(userId, cancellationToken);
        if (deletion is null)
        {
            return AccountDeletionResult.NothingToDelete;
        }

        // Both after the commit: the revocation so no other request can cache the account as it was before
        // (ITokenRevocationService), the files because storage can't roll back with the database.
        await EndSessionsAsync(userId);
        var (deleted, failed) = await DeleteFilesAsync(userId, deletion.Files);
        return deletion.Result with { FilesDeleted = deleted, FilesNotDeleted = failed };
    }

    private sealed record Deletion(AccountDeletionResult Result, IReadOnlyList<string> Files);

    private async Task<Deletion?> DeleteDataAsync(string userId, CancellationToken cancellationToken)
    {
        // The request's context may already track the user as it was read before this transaction (the handler reads
        // it with UserManager): start from what the database holds now.
        db.ChangeTracker.Clear();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Someone liking one of their comments or being notified by them at this moment can deadlock with this: the
        // deletion must not be the one that fails. Pooled connections reset the setting when they are reused.
        await db.Database.ExecuteSqlRawAsync("SET DEADLOCK_PRIORITY HIGH", cancellationToken);

        // Locked until the commit: a second deletion (a double tap) waits here, then finds nothing left to do, and
        // an update-me running meanwhile fails on the new concurrency stamp instead of writing the old data back.
        var user = await db.Users
            .FromSqlInterpolated($"SELECT * FROM AspNetUsers WITH (UPDLOCK, ROWLOCK) WHERE Id = {userId}")
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null || user.DeletedAt != null)
        {
            return null;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var files = new List<string>();
        if (!string.IsNullOrWhiteSpace(user.ProfilePhoto)) files.Add(user.ProfilePhoto);
        if (!string.IsNullOrWhiteSpace(user.ProfileBanner)) files.Add(user.ProfileBanner);
        files.AddRange(await db.ReadingLists
            .Where(rl => rl.UserId == userId && rl.CoverImageUrl != null && rl.CoverImageUrl != "")
            .Select(rl => rl.CoverImageUrl!)
            .ToListAsync(cancellationToken));

        await AnonymizeAsync(user, now, cancellationToken);

        // Soft delete, out of the rankings, as when an author deletes a novel: it disappears everywhere (lists,
        // search, sitemap), and readers who subscribed to it lose it.
        var novelsHidden = await db.Novels
            .Where(n => n.AuthorId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsDeleted, true)
                .SetProperty(n => n.IsEligibleForRanking, false), cancellationToken);

        await DeletePrivateDataAsync(userId, cancellationToken);
        var reportsClosed = await CloseReportsAboutAsync(userId, now, cancellationToken);
        var forfeited = await ForfeitBalanceAsync(userId, now, cancellationToken);

        // Nothing was deducted for them yet (a withdrawal is paid from the balance when approved), and the balance
        // is gone.
        var withdrawalsCancelled = await db.WithdrawalRequests
            .Where(r => r.UserId == userId && r.Status == RequestStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RequestStatus.Rejected)
                .SetProperty(r => r.ProcessedAt, (DateTime?)now)
                .SetProperty(r => r.ProcessedBy, (string?)null)
                .SetProperty(r => r.RejectionReason, DeletedAccounts.WithdrawalCancelledReason), cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new Deletion(
            new AccountDeletionResult(true, novelsHidden, forfeited, withdrawalsCancelled, reportsClosed),
            files.Distinct(StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// The user row stays, so what they wrote keeps an author: everything that identifies the person, or lets anyone
    /// sign in as them, is removed.
    /// </summary>
    private async Task AnonymizeAsync(User user, DateTime now, CancellationToken cancellationToken)
    {
        user.UserName = await DeletedUserNameAsync(user.Id, cancellationToken);
        user.NormalizedUserName = normalizer.NormalizeName(user.UserName);
        user.DisplayName = DeletedAccounts.DisplayName;
        user.Email = null;
        user.NormalizedEmail = null;
        user.EmailConfirmed = false;
        user.PhoneNumber = null;
        user.PhoneNumberConfirmed = false;
        user.UserBio = null;
        user.FacebookUrl = null;
        user.TwitterUrl = null;
        user.DiscordUrl = null;
        user.ProfilePhoto = null;
        user.ProfileBanner = null;

        // No way back in: no password, no two-factor, a new security stamp (reset and confirmation links die with the
        // old one), and every token refused (IsTokenActiveAsync also refuses a deleted account's tokens outright).
        user.PasswordHash = null;
        user.TwoFactorEnabled = false;
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        user.SecurityStamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        var cutoff = User.TokenCutoff(now);
        user.TokensValidAfter = user.TokensValidAfter > cutoff ? user.TokensValidAfter : cutoff;

        // Their library is deleted below, and the balance forfeited.
        user.LibraryNovelsCount = 0;
        user.PointBalance = 0;
        user.PointBalanceLastUpdated = now;
        user.DeletedAt = now;

        // The save also records the old user name for old profile links (ApplicationDbContext); a deleted account's
        // old names must lead nowhere, so all of them go.
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        await db.UserNameChanges.Where(c => c.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);

        // Google (and any other external) sign-ins, authenticator keys and the like.
        await db.UserLogins.Where(l => l.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        await db.UserTokens.Where(t => t.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        await db.UserClaims.Where(c => c.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>"deleted-" and the start of the user id, or all of it in the unlikely case that is taken.</summary>
    private async Task<string> DeletedUserNameAsync(string userId, CancellationToken cancellationToken)
    {
        var compact = new string(userId.Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var candidates = new[] { compact.Length > 12 ? compact[..12] : compact, compact, Guid.NewGuid().ToString("N") };
        foreach (var id in candidates.Where(c => c.Length > 0).Distinct())
        {
            var name = DeletedAccounts.UserNamePrefix + id;
            var normalized = normalizer.NormalizeName(name);
            if (!await db.Users.AnyAsync(u => u.NormalizedUserName == normalized && u.Id != userId, cancellationToken))
            {
                return name;
            }
        }
        return DeletedAccounts.UserNamePrefix + Guid.NewGuid().ToString("N");
    }

    private async Task DeletePrivateDataAsync(string userId, CancellationToken cancellationToken)
    {
        // Library and reading progress.
        await db.UserNovelProgress.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        // Their reading lists, with the lists' novels and everyone's follows of them (cascade).
        await db.ReadingLists.Where(rl => rl.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        // Follows both ways. Follower and following totals are counted from Follows, so that is the whole change.
        await db.Follows
            .Where(f => f.FollowerId == userId || f.FollowedId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        // Notifications to them go, with their pending pushes (cascade).
        await db.Notifications.Where(n => n.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        // Privilege subscriptions they bought (the ledger keeps the payments).
        await db.NovelPrivilegeSubscriptions.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        // Push devices and preferences.
        await db.UserDevices.Where(d => d.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.NotificationPreferences.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        // Blocks both ways; the blocked side first, since only BlockerId cascades from AspNetUsers.
        await db.UserBlocks.Where(b => b.BlockedId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.UserBlocks.Where(b => b.BlockerId == userId).ExecuteDeleteAsync(cancellationToken);

        // The rest in one batch: likes and list follows with the counters they add to, their name and photo on the
        // notifications they caused, and their places on the supporters board.
        await db.Database.ExecuteSqlRawAsync(
            DeleteCountedRowsSql,
            [UserIdParameter(userId), new SqlParameter("@deletedName", SqlDbType.NVarChar, 100) { Value = DeletedAccounts.DisplayName }],
            cancellationToken);
    }

    /// <summary>
    /// Each counter loses exactly the rows deleted here (OUTPUT), in the same atomic statements, as when the member
    /// unlikes or unfollows one by one.
    /// </summary>
    private const string DeleteCountedRowsSql = """
        DECLARE @comments TABLE (Id uniqueidentifier NOT NULL);
        DELETE FROM CommentLikes OUTPUT deleted.CommentId INTO @comments WHERE UserId = @userId;
        UPDATE c SET LikesCount = CASE WHEN c.LikesCount > x.Cnt THEN c.LikesCount - x.Cnt ELSE 0 END
        FROM Comments c JOIN (SELECT Id, COUNT(*) AS Cnt FROM @comments GROUP BY Id) x ON x.Id = c.Id;

        DECLARE @reviews TABLE (Id uniqueidentifier NOT NULL);
        DELETE FROM ReviewLikes OUTPUT deleted.ReviewId INTO @reviews WHERE UserId = @userId;
        UPDATE r SET LikeCount = CASE WHEN r.LikeCount > x.Cnt THEN r.LikeCount - x.Cnt ELSE 0 END
        FROM Reviews r JOIN (SELECT Id, COUNT(*) AS Cnt FROM @reviews GROUP BY Id) x ON x.Id = r.Id;

        DECLARE @posts TABLE (Id uniqueidentifier NOT NULL);
        DELETE FROM PostLikes OUTPUT deleted.PostId INTO @posts WHERE UserId = @userId;
        UPDATE p SET LikesCount = CASE WHEN p.LikesCount > x.Cnt THEN p.LikesCount - x.Cnt ELSE 0 END
        FROM Posts p JOIN (SELECT Id, COUNT(*) AS Cnt FROM @posts GROUP BY Id) x ON x.Id = p.Id;

        -- Other people's reading lists they followed (their own lists are gone with their followers).
        DECLARE @lists TABLE (Id uniqueidentifier NOT NULL);
        DELETE FROM ReadingListFollowers OUTPUT deleted.ReadingListId INTO @lists WHERE UserId = @userId;
        UPDATE rl SET FollowersCount = CASE WHEN rl.FollowersCount > x.Cnt THEN rl.FollowersCount - x.Cnt ELSE 0 END
        FROM ReadingLists rl JOIN (SELECT Id, COUNT(*) AS Cnt FROM @lists GROUP BY Id) x ON x.Id = rl.Id;

        -- The notifications they caused stay in other people's lists, without their name or photo. Every message about
        -- a member starts with the name they had then (ActorDisplayName), which is replaced too.
        UPDATE Notifications SET
            Message = CASE
                WHEN DATALENGTH(ActorDisplayName) > 0 AND LEFT(Message, DATALENGTH(ActorDisplayName) / 2) = ActorDisplayName
                THEN LEFT(@deletedName + SUBSTRING(Message, DATALENGTH(ActorDisplayName) / 2 + 1, 500), 500)
                ELSE Message END,
            ActorDisplayName = @deletedName,
            ActorProfilePhoto = NULL
        WHERE ActorId = @userId;

        -- Off the supporters board, the members below moving up a place.
        UPDATE l SET Rank = l.Rank - 1
        FROM GlobalSupporterLeaderboards l
        JOIN GlobalSupporterLeaderboards d ON d.Period = l.Period AND d.UserId = @userId AND l.Rank > d.Rank;
        DELETE FROM GlobalSupporterLeaderboards WHERE UserId = @userId;
        """;

    /// <summary>
    /// Open reports about them (their account, or content of theirs) close as <see cref="ReportAction.AccountDeleted"/>,
    /// with no admin; the reports they made stay open for the moderators.
    /// </summary>
    private Task<int> CloseReportsAboutAsync(string userId, DateTime now, CancellationToken cancellationToken)
    {
        var about = db.Reports.Where(r => r.Status == ReportStatus.Open);
        about = Guid.TryParse(userId, out var userGuid)
            ? about.Where(r => r.TargetOwnerId == userId || (r.TargetType == ReportTargetType.User && r.TargetId == userGuid))
            : about.Where(r => r.TargetOwnerId == userId);

        return about.ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, ReportStatus.Resolved)
            .SetProperty(r => r.Resolution, (ReportAction?)ReportAction.AccountDeleted)
            .SetProperty(r => r.ResolvedAt, (DateTime?)now)
            .SetProperty(r => r.ResolvedById, (string?)null), cancellationToken);
    }

    /// <summary>
    /// The balance goes to zero, whether points were left or owed (a refunded Play purchase), with a ledger row that
    /// says so; the ledger and Play purchases stay for accounting. Returns what was forfeited.
    /// </summary>
    private async Task<decimal> ForfeitBalanceAsync(string userId, DateTime now, CancellationToken cancellationToken)
    {
        var forfeited = new SqlParameter("@forfeited", SqlDbType.Decimal) { Precision = 18, Scale = 2, Direction = ParameterDirection.Output };
        await db.Database.ExecuteSqlRawAsync(
            """
            DECLARE @before TABLE (Balance decimal(18,2) NOT NULL);
            UPDATE UserWallets SET CurrentBalance = 0, UpdatedAt = @now
            OUTPUT deleted.CurrentBalance INTO @before
            WHERE UserId = @userId AND CurrentBalance <> 0;

            INSERT INTO PointTransactions (Id, UserId, Type, Amount, BalanceBefore, BalanceAfter, Description, RelatedRequestId, CreatedAt)
            SELECT NEWID(), @userId, @type, -Balance, Balance, 0, @description, NULL, @now FROM @before;

            SELECT @forfeited = ISNULL(SUM(Balance), 0) FROM @before;
            """,
            [
                UserIdParameter(userId),
                new SqlParameter("@now", SqlDbType.DateTime2) { Value = now },
                new SqlParameter("@type", SqlDbType.NVarChar, 50) { Value = TransactionType.BalanceForfeited },
                new SqlParameter("@description", SqlDbType.NVarChar, 500) { Value = ForfeitDescription },
                forfeited
            ],
            cancellationToken);
        return forfeited.Value is decimal amount ? amount : 0;
    }

    private static SqlParameter UserIdParameter(string userId) => new("@userId", SqlDbType.NVarChar, 450) { Value = userId };

    private async Task EndSessionsAsync(string userId)
    {
        try
        {
            await tokenRevocation.RevokeAllTokensAsync(userId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The deletion committed DeletedAt and a token cut-off, so the tokens are refused anyway; forgetting this
            // instance's cached state makes that immediate.
            tokenCutoffs.Forget(userId);
            logger.LogError(ex, "Account {UserId} was deleted, but revoking its sessions failed", userId);
        }
    }

    /// <summary>Their photo, banner and reading list covers, from storage. Failures are logged, never thrown.</summary>
    private async Task<(int Deleted, int Failed)> DeleteFilesAsync(string userId, IReadOnlyList<string> urls)
    {
        int deleted = 0, failed = 0;
        foreach (var url in urls)
        {
            var key = storage.KeyOf(url);
            if (key is null)
            {
                // Not one of ours (a Google account photo, for one); its address isn't logged, it points at the person.
                logger.LogInformation("Account {UserId} deleted: one of its images is not in Sard's storage, nothing to delete", userId);
                continue;
            }

            try
            {
                // Old uploads' keys came from user names and could be shared: never delete a file another account shows.
                if (await db.Users.AnyAsync(u => u.Id != userId && (u.ProfilePhoto == url || u.ProfileBanner == url)))
                {
                    logger.LogWarning("Account {UserId} deleted: kept {Key}, which another account also uses", userId, key);
                    continue;
                }

                await storage.DeleteAsync(key);
                deleted++;
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogError(ex, "Account {UserId} deleted, but its file {Key} could not be deleted from storage", userId, key);
            }
        }
        return (deleted, failed);
    }
}
