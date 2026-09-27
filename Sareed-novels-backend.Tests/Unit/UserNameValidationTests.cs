using Application.Users;
using Application.Users.Commands.CreateUser;
using Application.Users.Commands.UpdateMe;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>User names are public (profile links, @handles), so they can't hold an email address or any "@".</summary>
public class UserNameValidationTests
{
    private static CreateUserCommand SignUp(string userName) => new()
    {
        UserName = userName,
        Email = "reader@example.test",
        Password = "Correct-horse-1",
        DisplayName = "قارئ"
    };

    [Theory]
    [InlineData("reader@gmail.com")]
    [InlineData("reader@gmail.")]
    [InlineData("@reader")]
    [InlineData("ali@2024")]
    public void Sign_up_refuses_an_at_sign_in_the_user_name(string userName)
    {
        var result = new CreateUserCommandValidator().Validate(SignUp(userName));

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(CreateUserCommand.UserName), error.PropertyName);
        Assert.Equal(UserNameRules.NoAtSignMessage, error.ErrorMessage);
    }

    [Theory]
    [InlineData("reader.one_2")]
    [InlineData("sarduser123456")]
    [InlineData("Noor-Reader")]
    public void Sign_up_accepts_handles(string userName)
    {
        Assert.True(new CreateUserCommandValidator().Validate(SignUp(userName)).IsValid);
    }

    [Fact]
    public void Update_me_refuses_an_at_sign_and_leaves_an_absent_user_name_alone()
    {
        var validator = new UpdateMeCommandValidator();

        var refused = validator.Validate(new UpdateMeCommand { UserName = "me@gmail.com" });
        Assert.Equal(UserNameRules.NoAtSignMessage, Assert.Single(refused.Errors).ErrorMessage);
        Assert.True(validator.Validate(new UpdateMeCommand { UserName = "reader.one" }).IsValid);
        Assert.True(validator.Validate(new UpdateMeCommand { DisplayName = "قارئ جديد" }).IsValid);
    }
}
