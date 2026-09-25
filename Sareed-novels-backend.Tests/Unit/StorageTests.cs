using Domain.Exceptions;
using Application.Covers;
using Application.Novels.Commands.ChangeCover;
using Application.Services;
using Application.Users;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Services.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Unit;

public class StorageKeysTests
{
    private const string PublicBase = "https://pub-test.r2.dev";

    [Fact]
    public void Every_upload_gets_its_own_key_under_the_owner()
    {
        var owner = Guid.NewGuid().ToString();

        var first = StorageKeys.NewKey("novel-images", owner, "image/jpeg");
        var second = StorageKeys.NewKey("novel-images", owner, "image/jpeg");

        Assert.Matches($"^novel-images/{owner}/[0-9a-f]{{32}}\\.jpg$", first);
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("IMAGE/WEBP", ".webp")]
    [InlineData("image/jpeg; charset=binary", ".jpg")]
    [InlineData("application/octet-stream", "")]
    [InlineData(null, "")]
    public void The_extension_follows_the_content_type(string? contentType, string extension)
    {
        var fileName = StorageKeys.NewKey("x", "o", contentType).Split('/')[^1];

        Assert.Equal(32 + extension.Length, fileName.Length);
        Assert.EndsWith(extension, fileName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a/b")]
    public void Owner_ids_must_be_single_path_segments(string owner) =>
        Assert.Throws<ArgumentException>(() => StorageKeys.NewKey("novel-images", owner, "image/png"));

    [Fact]
    public void Public_urls_are_fully_encoded_so_browsers_and_apps_resolve_the_same_file()
    {
        // The production bug: "novel-images/امراة فى الظلام " with a trailing space. Browsers drop the space, apps keep it.
        var url = StorageKeys.PublicUrl(PublicBase + "/", "novel-images/امراة فى الظلام ");

        Assert.DoesNotContain(" ", url);
        Assert.EndsWith("%20", url);
        Assert.StartsWith(PublicBase + "/novel-images/", url);
    }

    [Fact]
    public void Keys_round_trip_through_their_public_url()
    {
        var key = StorageKeys.NewKey("profile-images", "user-1", "image/png");

        Assert.Equal(key, StorageKeys.KeyFrom(PublicBase, StorageKeys.PublicUrl(PublicBase, key)));
        Assert.Equal("profile-images/x", StorageKeys.KeyFrom(PublicBase, "profile-images/x"));
        Assert.Null(StorageKeys.KeyFrom(PublicBase, "https://elsewhere.example/profile-images/x"));
    }
}

public class ChangeCoverCommandHandlerTests
{
    private static (Novel Novel, INovelsRepository Novels, IUserContext User, IFormFile File) World()
    {
        var novel = new Novel { Id = Guid.NewGuid(), AuthorId = "author", Title = "امراة فى الظلام ", Slug = "s", Summary = "", CoverImageUrl = "old" };
        var novels = Substitute.For<INovelsRepository>();
        novels.GetOne(novel.Id).Returns(novel);
        var userContext = Substitute.For<IUserContext>();
        userContext.GetCurrentUser().Returns(new CurrentUser("author", "e", "u", "d"));
        var file = Substitute.For<IFormFile>();
        file.ContentType.Returns("image/png");
        file.OpenReadStream().Returns(new MemoryStream([1, 2, 3]));
        return (novel, novels, userContext, file);
    }

    [Fact]
    public async Task A_new_cover_is_stored_for_the_novel_and_only_its_url_column_is_updated()
    {
        var (novel, novels, userContext, file) = World();
        var covers = Substitute.For<INovelCoverService>();
        covers.StoreUploadAsync(novel.Id, Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns("https://cdn/new/960.webp");

        var handler = new ChangeCoverCommandHandler(NullLogger<ChangeCoverCommandHandler>.Instance, covers, userContext, novels);
        var result = await handler.Handle(new ChangerCoverCommand(novel.Id, file), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("https://cdn/new/960.webp", result.CoverImageUrl);
        await novels.Received(1).SetCoverUrlAsync(novel.Id, "https://cdn/new/960.webp", null, Arg.Any<CancellationToken>());
        await novels.DidNotReceive().UpdateOne(Arg.Any<Novel>());
    }

    [Fact]
    public async Task A_refused_image_is_a_failed_result_with_its_code_and_nothing_is_saved()
    {
        var (novel, novels, userContext, file) = World();
        var covers = Substitute.For<INovelCoverService>();
        covers.StoreUploadAsync(novel.Id, Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new CoverImageException(CoverErrorCodes.TooSmall, "too small"));

        var handler = new ChangeCoverCommandHandler(NullLogger<ChangeCoverCommandHandler>.Instance, covers, userContext, novels);
        var result = await handler.Handle(new ChangerCoverCommand(novel.Id, file), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(CoverErrorCodes.TooSmall, result.ErrorCode);
        await novels.DidNotReceive().SetCoverUrlAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Only_the_author_can_change_the_cover()
    {
        var (novel, novels, _, file) = World();
        var stranger = Substitute.For<IUserContext>();
        stranger.GetCurrentUser().Returns(new CurrentUser("someone-else", "e", "u", "d"));
        var covers = Substitute.For<INovelCoverService>();

        var handler = new ChangeCoverCommandHandler(NullLogger<ChangeCoverCommandHandler>.Instance, covers, stranger, novels);

        await Assert.ThrowsAsync<ForbidException>(() => handler.Handle(new ChangerCoverCommand(novel.Id, file), CancellationToken.None));
        await covers.DidNotReceive().StoreUploadAsync(Arg.Any<Guid>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }
}
