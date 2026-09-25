using Application.Novels.Commands.CreateNovel;
using Application.Novels.Commands.UpdateNovel;
using Application.Services;
using Application.Users;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>Genre ids chosen when a novel is created or edited: unknown ids, duplicates and more than four.</summary>
public class NovelGenreAssignmentTests : SqlServerDatabase
{
    private User author = default!;
    private List<Genre> genres = default!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await using var db = CreateContext();
        author = Seed.User();
        genres = Enumerable.Range(0, 5).Select(_ => Seed.Genre()).ToList();
        db.Users.Add(author);
        db.Genres.AddRange(genres);
        await db.SaveChangesAsync();
    }

    private int UnknownGenreId => genres.Max(g => g.Id) + 1000;

    private IUserContext AuthorContext()
    {
        var userContext = Substitute.For<IUserContext>();
        userContext.GetCurrentUser().Returns(new CurrentUser(author.Id, author.Email!, author.UserName!, author.DisplayName));
        return userContext;
    }

    private readonly IFileUploadService uploads = Substitute.For<IFileUploadService>();

    private CreateNovelCommandHandler CreateHandler(ApplicationDbContext db) => new(
        NullLogger<CreateNovelCommandHandler>.Instance,
        GenreWorld.Mapper,
        AuthorContext(),
        new GenresRepository(db),
        new NovelsRepository(db),
        uploads);

    private CreateNovelCommand NewNovel(string title, List<int> genreIds)
    {
        var cover = Substitute.For<IFormFile>();
        cover.ContentType.Returns("image/png");
        cover.OpenReadStream().Returns(_ => new MemoryStream([1, 2, 3]));
        uploads.UploadNovelImageAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns("https://example.test/cover.png");
        return new CreateNovelCommand { Title = title, Summary = "summary", CoverImageUrl = cover, GenreIds = genreIds };
    }

    private UpdateNovelCommandHandler UpdateHandler(ApplicationDbContext db) => new(
        NullLogger<UpdateNovelCommandHandler>.Instance,
        AuthorContext(),
        new NovelGenresRepository(db),
        new GenresRepository(db),
        new NovelsRepository(db),
        GenreWorld.Mapper,
        Substitute.For<INovelRecommendationService>());

    private async Task<Novel> NovelWithGenres(params Genre[] novelGenres)
    {
        await using var db = CreateContext();
        var novel = Seed.Novel(author, "novel " + Seed.Marker());
        db.Novels.Add(novel);
        db.NovelGenres.AddRange(novelGenres.Select(g => new NovelGenre { NovelId = novel.Id, GenreId = g.Id }));
        await db.SaveChangesAsync();
        return novel;
    }

    private async Task<List<int>> GenreIdsOf(Guid novelId)
    {
        await using var db = CreateContext();
        return await db.NovelGenres.Where(ng => ng.NovelId == novelId).Select(ng => ng.GenreId).OrderBy(id => id).ToListAsync();
    }

    [Fact]
    public async Task A_new_novel_is_saved_together_with_its_genres()
    {
        var title = "novel " + Seed.Marker();
        await using var db = CreateContext();

        var result = await CreateHandler(db).Handle(NewNovel(title, [genres[0].Id, genres[1].Id]), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(new[] { genres[0].Id, genres[1].Id }.Order(), await GenreIdsOf(result.NovelId!.Value));
    }

    [Fact]
    public async Task An_unknown_genre_creates_nothing()
    {
        var title = "novel " + Seed.Marker();
        await using var db = CreateContext();

        var result = await CreateHandler(db).Handle(NewNovel(title, [genres[0].Id, UnknownGenreId]), CancellationToken.None);

        Assert.False(result.Success);
        await uploads.DidNotReceiveWithAnyArgs().UploadNovelImageAsync(default!, default!, default!); // no orphan cover
        await using var check = CreateContext();
        Assert.False(await check.Novels.AnyAsync(n => n.Title == title)); // used to leave a novel with no genres
    }

    [Fact]
    public async Task Saving_the_same_genres_again_succeeds()
    {
        var novel = await NovelWithGenres(genres[0], genres[1]);
        await using var db = CreateContext();

        Assert.True(await new NovelGenresRepository(db).UpdateNovelGenres(novel.Id, [genres[1].Id, genres[0].Id]));
        Assert.Equal(new[] { genres[0].Id, genres[1].Id }.Order(), await GenreIdsOf(novel.Id));
    }

    [Fact]
    public async Task Bad_genre_lists_are_rejected_and_change_nothing()
    {
        var novel = await NovelWithGenres(genres[0]);
        var bad = new List<List<int>>
        {
            new(),                                           // none
            new() { genres[1].Id, UnknownGenreId },          // unknown id
            new() { genres[1].Id, genres[1].Id },            // duplicate
            genres.Select(g => g.Id).ToList()                // five
        };

        foreach (var genreIds in bad)
        {
            await using var db = CreateContext();
            var result = await UpdateHandler(db).Handle(
                new UpdateNovelCommand(novel.Id, "changed title", null, null, genreIds), CancellationToken.None);

            Assert.False(result.Success);
        }

        await using var check = CreateContext();
        Assert.Equal(novel.Title, (await check.Novels.SingleAsync(n => n.Id == novel.Id)).Title); // no half-applied edit
        Assert.Equal(new[] { genres[0].Id }, await GenreIdsOf(novel.Id));
    }

    [Fact]
    public async Task A_valid_edit_replaces_the_genres()
    {
        var novel = await NovelWithGenres(genres[0], genres[1]);
        await using var db = CreateContext();

        var result = await UpdateHandler(db).Handle(
            new UpdateNovelCommand(novel.Id, null, null, null, [genres[1].Id, genres[2].Id, genres[3].Id, genres[4].Id]),
            CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(genres.Skip(1).Select(g => g.Id).Order(), await GenreIdsOf(novel.Id));
    }

    [Fact]
    public void Validators_reject_duplicate_genres()
    {
        var create = new CreateNovelCommandValidator();
        Assert.Contains(create.Validate(new CreateNovelCommand { Title = "title", Summary = "summary", GenreIds = [1, 1] }).Errors,
            e => e.PropertyName == nameof(CreateNovelCommand.GenreIds));
        Assert.DoesNotContain(create.Validate(new CreateNovelCommand { Title = "title", Summary = "summary", GenreIds = [1, 2] }).Errors,
            e => e.PropertyName == nameof(CreateNovelCommand.GenreIds));

        var update = new UpdateNovelCommandValidator();
        Assert.False(update.Validate(new UpdateNovelCommandRequest { GenreIds = [2, 2] }).IsValid);
        Assert.False(update.Validate(new UpdateNovelCommandRequest { GenreIds = [1, 2, 3, 4, 5] }).IsValid);
        Assert.False(update.Validate(new UpdateNovelCommandRequest { GenreIds = [] }).IsValid);
        Assert.True(update.Validate(new UpdateNovelCommandRequest { GenreIds = null, Title = "new title" }).IsValid);
        Assert.True(update.Validate(new UpdateNovelCommandRequest { GenreIds = [1, 2] }).IsValid);
    }
}
