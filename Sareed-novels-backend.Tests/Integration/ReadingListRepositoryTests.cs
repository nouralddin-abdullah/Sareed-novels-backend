using Domain.Entities;
using Domain.ReadingLists;
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
    public async Task Saving_an_edit_keeps_the_counts_changed_since_the_list_was_read()
    {
        var owner = Seed.User();
        var list = List(owner, isPublic: true);
        await using (var db = database.CreateContext())
        {
            db.Users.Add(owner);
            db.ReadingLists.Add(list);
            await db.SaveChangesAsync();
        }

        await using var editing = database.CreateContext();
        var repository = new ReadingListsRepository(editing);
        var read = (await repository.GetByIdAsync(list.Id))!;

        // While the edit is in progress (a picture upload takes seconds), a novel is added and someone follows the list.
        await using (var other = database.CreateContext())
        {
            await new ReadingListsRepository(other).AdjustNovelsCountAsync(list.Id, +1);
            await new ReadingListsRepository(other).AdjustFollowersCountAsync(list.Id, +1);
        }

        read.Description = "وصف جديد";
        Assert.True(await repository.UpdateAsync(read));

        await using var check = database.CreateContext();
        var saved = await check.ReadingLists.SingleAsync(rl => rl.Id == list.Id);
        Assert.Equal("وصف جديد", saved.Description);
        Assert.Equal(1, saved.NovelsCount);
        Assert.Equal(1, saved.FollowersCount);
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

    [Fact]
    public async Task Whether_each_list_has_a_novel_comes_from_the_same_queries_as_the_page()
    {
        var owner = Seed.User();
        var author = Seed.User();
        var novel = Seed.Novel(author, "رواية " + Seed.Marker());
        var draft = Seed.Novel(author, "مسودة " + Seed.Marker(), isDraft: true);
        var lists = Enumerable.Range(0, 4).Select(i => List(owner, isPublic: i % 2 == 0, DateTime.UtcNow.AddMinutes(-i))).ToList();
        await using (var db = database.CreateContext())
        {
            db.Users.AddRange(owner, author);
            db.Novels.AddRange(novel, draft);
            db.ReadingLists.AddRange(lists);
            db.ReadingListNovels.AddRange(
                new ReadingListNovel { ReadingListId = lists[0].Id, NovelId = novel.Id },
                new ReadingListNovel { ReadingListId = lists[2].Id, NovelId = novel.Id, OrderIndex = 1 },
                new ReadingListNovel { ReadingListId = lists[2].Id, NovelId = draft.Id },
                new ReadingListNovel { ReadingListId = lists[3].Id, NovelId = draft.Id });
            await db.SaveChangesAsync();
        }

        async Task<(List<bool?> Contains, CommandLog Log)> Page(Guid? containsNovelId)
        {
            var log = new CommandLog();
            await using var db = database.CreateContext(log);
            var (page, total) = await new ReadingListsRepository(db).GetUserReadingListsWithPreviewAsync(owner.Id, 1, 12, containsNovelId);
            Assert.Equal(4, total);
            Assert.Equal(lists.Select(l => l.Id), page.Select(s => s.List.Id));
            return (page.Select(s => s.ContainsNovel).ToList(), log);
        }

        var (notAsked, plainLog) = await Page(null);
        var (withNovel, novelLog) = await Page(novel.Id);
        var (withDraft, _) = await Page(draft.Id);
        var (withUnknown, _) = await Page(Guid.NewGuid());

        Assert.All(notAsked, Assert.Null);
        Assert.Equal([true, false, true, false], withNovel);
        // A novel readers can't open (a draft since it was added) is still on the list.
        Assert.Equal([false, false, true, true], withDraft);
        Assert.Equal([false, false, false, false], withUnknown);

        // The same commands (count, page, previews) with or without the novel: no query per list.
        Assert.Equal(plainLog.Commands.Count, novelLog.Commands.Count);
        Assert.Contains(novelLog.Commands, sql => sql.Contains("EXISTS"));
        Assert.DoesNotContain(plainLog.Commands, sql => sql.Contains("EXISTS"));
    }

    [Fact]
    public async Task A_lists_novels_load_with_their_authors_in_three_queries_however_many_there_are()
    {
        var owner = Seed.User();
        var authors = Enumerable.Range(0, 4).Select(i => Seed.User(displayName: $"كاتب {i}")).ToList();
        authors[1].ProfilePhoto = "https://files.test/profile-images/photo.webp";
        var novels = authors.Select(a => Seed.Novel(a, "رواية " + Seed.Marker())).ToList();
        var deleted = Seed.Novel(authors[0], "محذوفة " + Seed.Marker());
        deleted.IsDeleted = true;
        var (small, large) = (List(owner, isPublic: true), List(owner, isPublic: true));
        await using (var db = database.CreateContext())
        {
            db.Users.AddRange(authors.Append(owner));
            db.Novels.AddRange(novels.Append(deleted));
            db.ReadingLists.AddRange(small, large);
            db.ReadingListNovels.Add(new ReadingListNovel { ReadingListId = small.Id, NovelId = novels[0].Id });
            db.ReadingListNovels.AddRange(novels.Append(deleted).Select((n, i) => new ReadingListNovel
            {
                ReadingListId = large.Id, NovelId = n.Id, OrderIndex = i
            }));
            await db.SaveChangesAsync();
        }

        async Task<(ReadingList List, CommandLog Log)> Load(ReadingList list)
        {
            var log = new CommandLog();
            await using var db = database.CreateContext(log);
            return ((await new ReadingListsRepository(db).GetByIdWithDetailsAsync(list.Id))!, log);
        }

        var (one, oneLog) = await Load(small);
        var (four, fourLog) = await Load(large);

        Assert.Equal(authors[0].DisplayName, Assert.Single(one.Novels).Novel.Owner.DisplayName);
        // The deleted novel doesn't load; every other one comes with its author (#59).
        Assert.Equal(novels.Select(n => (n.Id, n.AuthorId)).Order(), four.Novels.Select(rln => (rln.Novel.Id, rln.Novel.Owner.Id)).Order());
        Assert.All(four.Novels, rln =>
        {
            var author = authors.Single(a => a.Id == rln.Novel.AuthorId);
            Assert.Equal((author.UserName, author.DisplayName, author.ProfilePhoto),
                (rln.Novel.Owner.UserName, rln.Novel.Owner.DisplayName, rln.Novel.Owner.ProfilePhoto));
        });
        // The list with its owner, the novels with their authors, their genres: as many queries as before #59, for one
        // novel or four.
        Assert.Equal(3, oneLog.Commands.Count);
        Assert.Equal(3, fourLog.Commands.Count);
    }

    [Fact]
    public async Task A_removal_or_an_add_during_a_reorder_waits_for_it()
    {
        var owner = Seed.User();
        var list = List(owner, isPublic: false);
        var novels = Enumerable.Range(0, 4).Select(i => Seed.Novel(owner, $"رواية {i} {Seed.Marker()}")).ToList();
        var (a, b, c, added) = (novels[0].Id, novels[1].Id, novels[2].Id, novels[3].Id);
        await using (var db = database.CreateContext())
        {
            db.Users.Add(owner);
            db.ReadingLists.Add(list);
            db.Novels.AddRange(novels);
            db.ReadingListNovels.AddRange(new[] { a, b, c }.Select((id, i) => new ReadingListNovel
            {
                ReadingListId = list.Id, NovelId = id, OrderIndex = i, AddedAt = DateTime.UtcNow.AddMinutes(i - 10)
            }));
            await db.SaveChangesAsync();
        }

        // Paused after checking the ids against the list, before writing the order.
        var pause = new PauseBefore("[UpdatedAt]");
        await using var reordering = database.CreateContext(pause);
        var reorder = new ReadingListNovelsRepository(reordering).ReorderAsync(list.Id, [c, b, a]);
        await pause.Reached.WaitAsync();

        await using var removing = database.CreateContext();
        await using var adding = database.CreateContext();
        var removal = new ReadingListNovelsRepository(removing).RemoveNovelAsync(list.Id, b);
        var add = new ReadingListNovelsRepository(adding).AddNovelAsync(new ReadingListNovel
        {
            ReadingListId = list.Id, NovelId = added, OrderIndex = 3, AddedAt = DateTime.UtcNow
        });
        // Without the locks the removal went through here, and writing b's new place then failed (a 500).
        var first = await Task.WhenAny(removal, add, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.False(first == removal || first == add, "a removal or an add got past a reorder in progress");

        pause.Release();
        Assert.Equal(ReadingListReorderResult.Reordered, await reorder);
        Assert.True(await removal);
        Assert.True(await add);

        await using var check = database.CreateContext();
        var order = await check.ReadingListNovels.Where(r => r.ReadingListId == list.Id)
            .OrderBy(r => r.OrderIndex).Select(r => new { r.NovelId, r.OrderIndex }).ToListAsync();
        Assert.Equal([(c, 0), (a, 2), (added, 3)], order.Select(r => (r.NovelId, r.OrderIndex)));
    }
}
