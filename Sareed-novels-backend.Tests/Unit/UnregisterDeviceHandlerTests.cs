using Application.Notifications.Commands.UnregisterDevice;
using Application.Users;
using Domain.Entities;
using Domain.Repositories;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// Unregistering a push device works without a valid session (the app signed out after a 401, or offline): the FCM
/// token is the proof. Signed in, only the caller's own registration goes, as before.
/// </summary>
public class UnregisterDeviceHandlerTests
{
    private readonly IUserDevicesRepository devices = Substitute.For<IUserDevicesRepository>();
    private readonly IUserContext userContext = Substitute.For<IUserContext>();

    private Task Unregister(string token) =>
        new UnregisterDeviceCommandHandler(devices, userContext).Handle(new UnregisterDeviceCommand(token), CancellationToken.None);

    [Fact]
    public async Task Signed_in_it_removes_only_the_callers_own_registration()
    {
        userContext.GetCurrentUser().Returns(new CurrentUser("user-1", "a@example.test", "sara", "سارة"));

        await Unregister(" fcm-token-1 ");

        await devices.Received(1).Remove("user-1", "fcm-token-1");
        await devices.DidNotReceiveWithAnyArgs().RemoveToken(default!);
    }

    [Fact]
    public async Task Without_a_session_it_removes_the_token_whoever_registered_it()
    {
        userContext.GetCurrentUser().Returns((CurrentUser?)null);

        await Unregister("fcm-token-1");

        await devices.Received(1).RemoveToken("fcm-token-1");
        await devices.DidNotReceiveWithAnyArgs().Remove(default!, default!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_token_removes_nothing(string token)
    {
        userContext.GetCurrentUser().Returns((CurrentUser?)null);

        await Unregister(token);

        Assert.Empty(devices.ReceivedCalls());
    }

    [Fact]
    public async Task A_token_longer_than_any_registered_one_removes_nothing()
    {
        userContext.GetCurrentUser().Returns((CurrentUser?)null);

        await Unregister(new string('a', UserDevice.TokenMaxLength + 1));

        Assert.Empty(devices.ReceivedCalls());
    }
}
