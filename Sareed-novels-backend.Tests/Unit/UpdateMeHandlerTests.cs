using Application.Services;
using Application.Users;
using Application.Users.Commands.UpdateMe;
using AutoMapper;
using Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>update-me says which picture failed to upload (#25): UploadFailed with field ProfilePhoto or ProfileBanner.</summary>
public class UpdateMeHandlerTests
{
    private readonly User user = new() { Id = Guid.NewGuid().ToString(), UserName = "noor", DisplayName = "نور" };
    private readonly UserManager<User> users =
        Substitute.For<UserManager<User>>(Substitute.For<IUserStore<User>>(), null, null, null, null, null, null, null, null);
    private readonly IFileUploadService uploads = Substitute.For<IFileUploadService>();

    public UpdateMeHandlerTests()
    {
        users.FindByIdAsync(user.Id).Returns(user);
    }

    private Task<UpdateMeResult> Update(UpdateMeCommand command)
    {
        var userContext = Substitute.For<IUserContext>();
        userContext.GetCurrentUser().Returns(new CurrentUser(user.Id, "noor@example.test", user.UserName!, user.DisplayName));
        return new UpdateMeCommandHandler(NullLogger<UpdateMeCommandHandler>.Instance, uploads, userContext, users, Substitute.For<IMapper>(),
                Substitute.For<ISender>())
            .Handle(command, CancellationToken.None);
    }

    private static IFormFile Image(string field) =>
        new FormFile(new MemoryStream([0x89, 0x50, 0x4E, 0x47]), 0, 4, field, field + ".png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };

    [Fact]
    public async Task A_failed_photo_upload_says_it_was_the_photo()
    {
        uploads.UploadImageAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), user.Id).ThrowsAsync(new IOException("R2 is down"));

        var result = await Update(new UpdateMeCommand { ProfilePhoto = Image("ProfilePhoto"), ProfileBanner = Image("ProfileBanner") });

        Assert.Equal(("UploadFailed", "ProfilePhoto"), (result.Code, result.Field));
        Assert.False(result.Success);
        await users.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
    }

    [Fact]
    public async Task A_failed_banner_upload_says_it_was_the_banner()
    {
        uploads.UploadImageAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), user.Id).Returns("https://files.test/photo.png");
        uploads.UploadProfileBannerAsync(Arg.Any<Stream>(), Arg.Any<string>(), user.Id).ThrowsAsync(new IOException("R2 is down"));

        var result = await Update(new UpdateMeCommand { ProfilePhoto = Image("ProfilePhoto"), ProfileBanner = Image("ProfileBanner") });

        Assert.Equal(("UploadFailed", "ProfileBanner"), (result.Code, result.Field));
        Assert.Equal("تعذّر رفع صورة الغلاف. حاول مرة أخرى.", result.Message);
        await users.DidNotReceiveWithAnyArgs().UpdateAsync(default!);
    }
}
