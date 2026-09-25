using Domain.Entities;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

public class ReadingListRepositoryTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    private static ReadingList List(User owner, bool isPublic, DateTime? updatedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = owner.Id,
        Name = "قائمة " + Seed.Marker(),
        IsPublic = isPublic,
        UpdatedAt = updatedAt ?? DateTime.UtcNow
    };

    [Fact]
    public async Task Followed_lists_leave_out_lists_their_owner_made_private()
    {
        await using (var db = database.CreateContext())
        {
            var owner = Seed.User();
            var follower = Seed.User();
            var stillPublic = List(owner, isPublic: true);
            var nowPrivate = List(owner, isPublic: false);
            db.Users.AddRange(owner, follower);
            db.ReadingLists.AddRange(stillPublic, nowPrivate);
            db.ReadingListFollowers.AddRange(
                new ReadingListFollower { ReadingListId = stillPublic.Id, UserId = follower.Id },
                new ReadingListFollower { ReadingListId = nowPrivate.Id, UserId = follower.Id });
            await db.SaveChangesAsync();

            await using var check = database.CreateContext();
            var (lists, total) = await new ReadingListsRepository(check).GetFollowedReadingListsWithPreviewAsync(follower.Id, 1, 12);

            Assert.Equal(1, total);
            Assert.Equal(stillPublic.Id, Assert.Single(lists).List.Id);
        }
    }

    [Fact]
    public async Task Previews_and_counts_show_only_novels_readers_can_open_in_list_order()
    {
        var owner = Seed.User();
        var list = List(owner, isPublic: true);
        var novels = Enumerable.Range(0, 8).Select(i => Seed.Novel(owner, $"رواية {i} {Seed.Marker()}")).ToList();
        novels[0].IsDraft = true;
        novels[1].IsDeleted = true;
        await using (var db = database.CreateContext())
        {
            db.Users.Add(owner);
            db.ReadingLists.Add(list);
            db.Novels.AddRange(novels);
            // Added in reverse so list order (OrderIndex), not insertion order, decides the preview.
            db.ReadingListNovels.AddRange(novels.Select((n, i) => new ReadingListNovel
            {
                ReadingListId = list.Id, NovelId = n.Id, OrderIndex = i, AddedAt = DateTime.UtcNow.AddMinutes(-i)
            }).Reverse());
            await db.SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        var (lists, total) = await new ReadingListsRepository(check).GetUserPublicReadingListsWithPreviewAsync(owner.Id, 1, 12);

        Assert.Equal(1, total);
        var summary = Assert.Single(lists);
        Assert.Equal(6, summary.VisibleNovelsCount);
        Assert.Equal(novels.Skip(2).Take(5).Select(n => n.Id), summary.PreviewNovels.Select(p => p.NovelId));
    }

    [Fact]
    public async Task Pages_are_ordered_by_last_update_and_private_lists_stay_off_public_pages()
    {
        var owner = Seed.User();
        var lists = Enumerable.Range(0, 5).Select(i => List(owner, isPublic: i != 2, DateTime.UtcNow.AddHours(-i))).ToList();
        await using (var db = database.CreateContext())
        {
            db.Users.Add(owner);
            db.ReadingLists.AddRange(lists);
            await db.SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        var repository = new ReadingListsRepository(check);
        var (publicPage2, publicTotal) = await repository.GetUserPublicReadingListsWithPreviewAsync(owner.Id, 2, 2);
        var (minePage1, mineTotal) = await repository.GetUserReadingListsWithPreviewAsync(owner.Id, 1, 3);

        Assert.Equal(4, publicTotal);
        Assert.Equal(new[] { lists[3].Id, lists[4].Id }, publicPage2.Select(s => s.List.Id));
        Assert.Equal(5, mineTotal);
        Assert.Equal(new[] { lists[0].Id, lists[1].Id, lists[2].Id }, minePage1.Select(s => s.List.Id));
    }

    [Fact]
    public async Task Counter_updates_are_atomic_and_never_go_negative()
    {
        var owner = Seed.User();
        var list = List(owner, isPublic: true);
        await using (var db = database.CreateContext())
        {
            db.Users.Add(owner);
            db.ReadingLists.Add(list);
            await db.SaveChangesAsync();
        }

        await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var db = database.CreateContext();
            var repository = new ReadingListsRepository(db);
            await repository.AdjustNovelsCountAsync(list.Id, +1);
            await repository.AdjustFollowersCountAsync(list.Id, +1);
        }));

        await using (var db = database.CreateContext())
        {
            var counted = await db.ReadingLists.SingleAsync(rl => rl.Id == list.Id);
            Assert.Equal(20, counted.NovelsCount);
            Assert.Equal(20, counted.FollowersCount);
            await new ReadingListsRepository(db).AdjustFollowersCountAsync(list.Id, -25);
        }

        await using var check = database.CreateContext();
        Assert.Equal(0, (await check.ReadingLists.SingleAsync(rl => rl.Id == list.Id)).FollowersCount);
    }

    [Fact]
    public async Task A_novel_added_after_a_removal_goes_to_the_end_of_the_list()
    {
        var owner = Seed.User();
        var list = List(owner, isPublic: false);
        var novels = Enumerable.Range(0, 3).Select(i => Seed.Novel(owner, $"رواية {i} {Seed.Marker()}")).ToList();
        await using (var db = database.CreateContext())
        {
            db.Users.Add(owner);
            db.ReadingLists.Add(list);
            db.Novels.AddRange(novels);
            db.ReadingListNovels.AddRange(
                new ReadingListNovel { ReadingListId = list.Id, NovelId = novels[0].Id, OrderIndex = 0 },
                new ReadingListNovel { ReadingListId = list.Id, NovelId = novels[1].Id, OrderIndex = 1 });
            await db.SaveChangesAsync();
            await new ReadingListNovelsRepository(db).RemoveNovelAsync(list.Id, novels[0].Id);
        }

        await using var check = database.CreateContext();
        var repository = new ReadingListNovelsRepository(check);
        Assert.Equal(2, await repository.GetNextOrderIndexAsync(list.Id));
        Assert.Equal(0, await repository.GetNextOrderIndexAsync(Guid.NewGuid()));
    }
}
