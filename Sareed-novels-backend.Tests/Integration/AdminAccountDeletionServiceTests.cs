using Application.Services;
using Domain.Entities;
using Domain.Moderation;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// AccountDeletionService.DeleteByAdminAsync on its own: the admin audit row is written in the deletion's transaction,
/// so there is one exactly when the account was deleted, and it holds ids and the admin's words only.
/// </summary>
public class AdminAccountDeletionServiceTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private const string Bucket = "https://files.test";

    private static AccountDeletionService Service(ApplicationDbContext db, IObjectStorage storage)
    {
        var cache = new TokenCutoffCache(new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }));
        return new AccountDeletionService(db, new UpperInvariantLookupNormalizer(),
            new TokenRevocationService(db, cache, TimeProvider.System), cache, storage, TimeProvider.System,
            new ListLogger<AccountDeletionService>());
    }

    /// <summary>A member with a bio, a photo in storage, a novel and 250 points.</summary>
    private static async Task<(User Member, Novel Novel, string PhotoKey)> SeedMember(ApplicationDbContext db, InMemoryObjectStorage storage)
    {
        var member = Seed.User(displayName: "اسم العضو");
        member.UserBio = "نبذة العضو";
        var photoKey = $"profile-images/{member.Id}/photo.png";
        member.ProfilePhoto = await storage.PutAsync(photoKey, new MemoryStream([1, 2, 3]), "image/png");
        member.PointBalance = 250;
        var novel = Seed.Novel(member, "رواية " + Seed.Marker());
        db.Users.Add(member);
        db.Novels.Add(novel);
        db.UserWallets.Add(new UserWallet { Id = Guid.NewGuid(), UserId = member.Id, CurrentBalance = 250 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (member, novel, photoKey);
    }

    private static Task<List<AdminAuditLog>> AuditRows(ApplicationDbContext db, string userId) =>
        db.AdminAuditLogs.AsNoTracking().Where(a => a.TargetUserId == userId).ToListAsync();

    [Fact]
    public async Task The_audit_row_is_written_with_the_deletion_and_holds_ids_and_the_admins_note_only()
    {
        await using var db = database.CreateContext();
        var storage = new InMemoryObjectStorage(Bucket);
        var (member, novel, photoKey) = await SeedMember(db, storage);
        var adminId = Guid.NewGuid().ToString();

        var result = await Service(db, storage).DeleteByAdminAsync(member.Id,
            new AdminAccountDeletion(adminId, AccountDeletionReason.Underage, "بلاغ الدعم رقم 42"));

        // The same deletion as the member's own.
        Assert.True(result.Deleted);
        Assert.Equal(1, result.NovelsHidden);
        Assert.Equal(250, result.ForfeitedBalance);
        Assert.Equal(1, result.FilesDeleted);
        Assert.False(storage.Objects.ContainsKey(photoKey));
        db.ChangeTracker.Clear();
        var row = await db.Users.AsNoTracking().SingleAsync(u => u.Id == member.Id);
        Assert.NotNull(row.DeletedAt);
        Assert.Equal(row.DeletedAt, result.DeletedAt);
        Assert.True((await db.Novels.IgnoreQueryFilters().AsNoTracking().SingleAsync(n => n.Id == novel.Id)).IsDeleted);

        var audit = Assert.Single(await AuditRows(db, member.Id));
        Assert.Equal(adminId, audit.AdminId);
        Assert.Equal(AdminAuditAction.DeleteAccount, audit.Action);
        Assert.Equal("Underage", audit.Reason);
        Assert.Equal("بلاغ الدعم رقم 42", audit.Note);
        Assert.Equal(row.DeletedAt, audit.CreatedAt);
        // Enums are stored by name, like the reports'.
        Assert.Equal("DeleteAccount", await db.Database
            .SqlQuery<string>($"SELECT Action AS Value FROM AdminAuditLogs WHERE Id = {audit.Id}").SingleAsync());

        // Nothing of the member is copied into it.
        var stored = new[] { audit.AdminId, audit.Action.ToString(), audit.TargetUserId, audit.Reason, audit.Note };
        foreach (var personal in new[] { member.UserName!, member.Email!, member.DisplayName, member.UserBio! })
        {
            Assert.DoesNotContain(stored, value => value != null && value.Contains(personal, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Nothing_to_delete_writes_no_audit_row_and_a_members_own_deletion_writes_none()
    {
        await using var db = database.CreateContext();
        var storage = new InMemoryObjectStorage(Bucket);
        var (byAdmin, _, _) = await SeedMember(db, storage);
        var (bySelf, _, _) = await SeedMember(db, storage);
        var deletion = new AdminAccountDeletion(Guid.NewGuid().ToString(), AccountDeletionReason.PolicyViolation, null);

        Assert.True((await Service(db, storage).DeleteByAdminAsync(byAdmin.Id, deletion)).Deleted);
        var again = await Service(db, storage).DeleteByAdminAsync(byAdmin.Id, deletion);
        Assert.False(again.Deleted);
        Assert.Null(again.DeletedAt);
        var unknown = Guid.NewGuid().ToString();
        Assert.False((await Service(db, storage).DeleteByAdminAsync(unknown, deletion)).Deleted);
        Assert.True((await Service(db, storage).DeleteAsync(bySelf.Id)).Deleted);

        db.ChangeTracker.Clear();
        var audit = Assert.Single(await AuditRows(db, byAdmin.Id));
        Assert.Null(audit.Note);
        Assert.Empty(await AuditRows(db, unknown));
        Assert.Empty(await AuditRows(db, bySelf.Id));
    }

    [Fact]
    public async Task An_audit_row_that_cannot_be_written_leaves_the_account_as_it_was()
    {
        await using var db = database.CreateContext();
        var storage = new InMemoryObjectStorage(Bucket);
        var (member, novel, photoKey) = await SeedMember(db, storage);
        var before = await db.Users.AsNoTracking().SingleAsync(u => u.Id == member.Id);
        // Longer than the column: the handler's validator never lets this through, so the insert fails in SQL Server,
        // after every other step of the deletion.
        var tooLong = new AdminAccountDeletion(Guid.NewGuid().ToString(), AccountDeletionReason.OwnerRequest, new string('ن', AdminAuditLog.NoteMaxLength + 1));

        await Assert.ThrowsAsync<DbUpdateException>(() => Service(db, storage).DeleteByAdminAsync(member.Id, tooLong));

        // Rolled back as a whole: the member, their novel and their points are as they were, and their photo is still in
        // storage (files go only after the commit).
        await using var check = database.CreateContext();
        var row = await check.Users.AsNoTracking().SingleAsync(u => u.Id == member.Id);
        Assert.Null(row.DeletedAt);
        Assert.Equal(before.UserName, row.UserName);
        Assert.Equal(before.Email, row.Email);
        Assert.Equal(before.DisplayName, row.DisplayName);
        Assert.Equal(before.SecurityStamp, row.SecurityStamp);
        Assert.Equal(250, row.PointBalance);
        Assert.Equal(250, (await check.UserWallets.AsNoTracking().SingleAsync(w => w.UserId == member.Id)).CurrentBalance);
        Assert.False(await check.PointTransactions.AnyAsync(t => t.UserId == member.Id));
        Assert.False((await check.Novels.IgnoreQueryFilters().AsNoTracking().SingleAsync(n => n.Id == novel.Id)).IsDeleted);
        Assert.True(storage.Objects.ContainsKey(photoKey));
        Assert.Empty(await AuditRows(check, member.Id));

        // And the account can still be deleted afterwards.
        Assert.True((await Service(check, storage).DeleteByAdminAsync(member.Id, tooLong with { Note = "طلب بالبريد" })).Deleted);
        Assert.Single(await AuditRows(check, member.Id));
    }
}
