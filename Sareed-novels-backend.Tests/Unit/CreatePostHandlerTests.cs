using Application.Posts;
using Application.Posts.Commands.CreatePost;
using Application.Posts.DTOs;
using Application.Posts.Queries.GetPost;
using Application.Services;
using Application.Users;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sareed_novels_backend.Tests.Integration;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// A new post's picture (#43): a failed upload is UploadFailed, logged as an error with its exception, and no post is
/// saved; a cancelled request isn't a failed upload; a picture stored for a post that then can't be saved is deleted.
/// </summary>
public class CreatePostHandlerTests
{
    private const string StoredUrl = "https://files.test/post-images/p/picture.png";

    private readonly IFileUploadService uploads = Substitute.For<IFileUploadService>();
    private readonly IPostsRepository posts = Substitute.For<IPostsRepository>();
    private readonly ListLogger<CreatePostCommandHandler> logger = new();

    private Task<CreatePostResult> Create(CancellationToken cancellationToken = default)
    {
        var userContext = Substitute.For<IUserContext>();
        userContext.GetCurrentUser().Returns(new CurrentUser("user-1", "noor@example.test", "noor", "نور"));
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetPostQuery>(), Arg.Any<CancellationToken>()).Returns(new PostDTO());

        var picture = new FormFile(new MemoryStream([0x89, 0x50, 0x4E, 0x47]), 0, 4, "Image", "picture.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
        return new CreatePostCommandHandler(logger, new CreatePostCommandValidator(), posts, Substitute.For<INovelsRepository>(),
                userContext, uploads, sender)
            .Handle(new CreatePostCommand("منشور بصورة", picture, null), cancellationToken);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(TaskCanceledException))] // the storage client's own timeout: nobody cancelled the request
    public async Task A_failed_upload_is_UploadFailed_logged_as_an_error_and_saves_no_post(Type failure)
    {
        var exception = (Exception)Activator.CreateInstance(failure, "R2 is down")!;
        uploads.UploadPostImageAsync(Arg.Any<Stream>(), "image/png", Arg.Any<string>()).ThrowsAsync(exception);

        var result = await Create();

        Assert.False(result.Success);
        Assert.Equal((PostRules.UploadFailedCode, "تعذّر رفع الصورة، حاول مرة أخرى."), (result.Code, result.Message));
        Assert.Null(result.Post);
        var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Same(exception, error.Exception);
        await posts.DidNotReceiveWithAnyArgs().CreatePost(default!);
    }

    [Fact]
    public async Task A_request_cancelled_during_the_upload_is_not_UploadFailed()
    {
        using var request = new CancellationTokenSource();
        uploads.UploadPostImageAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>()).Returns<Task<string>>(_ =>
        {
            request.Cancel();
            throw new OperationCanceledException(request.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(request.Token));

        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Error);
        await posts.DidNotReceiveWithAnyArgs().CreatePost(default!);
    }

    [Fact]
    public async Task When_the_post_cannot_be_saved_its_stored_picture_is_deleted_and_the_error_stands()
    {
        var saveFailure = new InvalidOperationException("The database is down");
        uploads.UploadPostImageAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>()).Returns(StoredUrl);
        uploads.DeleteImageAsync(StoredUrl).Returns(true);
        posts.CreatePost(Arg.Is<Post>(p => p.ImageUrl == StoredUrl)).ThrowsAsync(saveFailure);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => Create());

        Assert.Same(saveFailure, thrown);
        await uploads.Received(1).DeleteImageAsync(StoredUrl);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_picture_that_cannot_be_deleted_either_is_logged_and_the_save_error_stands(bool deleteThrows)
    {
        var saveFailure = new InvalidOperationException("The database is down");
        uploads.UploadPostImageAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>()).Returns(StoredUrl);
        if (deleteThrows)
        {
            uploads.DeleteImageAsync(StoredUrl).ThrowsAsync(new HttpRequestException("R2 is down"));
        }
        else
        {
            uploads.DeleteImageAsync(StoredUrl).Returns(false);
        }
        posts.CreatePost(Arg.Any<Post>()).ThrowsAsync(saveFailure);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => Create());

        Assert.Same(saveFailure, thrown);
        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("could not be deleted", warning.Message);
        Assert.Equal(StoredUrl, warning.Values["ImageUrl"]);
    }
}
