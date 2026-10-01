using Application.Users.DTOS;
using Domain.Exceptions;
using Domain.Moderation;
using Domain.Profiles;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.UpdateListPrivacy;

public class UpdateListPrivacyCommandHandler(
    ILogger<UpdateListPrivacyCommandHandler> logger,
    IUsersRepository usersRepository,
    IUserContext userContext) : IRequestHandler<UpdateListPrivacyCommand, ListPrivacyDto>
{
    public async Task<ListPrivacyDto> Handle(UpdateListPrivacyCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        var privacy = await usersRepository.SetListPrivacyAsync(currentUser.Id, Parse(request.Reviews),
            Parse(request.Comments), cancellationToken) ?? throw ProfileLookup.NotFound();
        logger.LogInformation("User {UserId} shows their reviews to {Reviews} and their comments to {Comments}",
            currentUser.Id, privacy.Reviews, privacy.Comments);

        return ListPrivacyDto.From(privacy);
    }

    /// <summary>A value sent, in its canonical case; null when left out. The validator refused anything else.</summary>
    private static ListVisibility? Parse(string? value) =>
        value is null ? null
        : EnumNames.TryParse<ListVisibility>(value, out var visibility) ? visibility
        : throw new ArgumentException($"Not a list visibility: {value}", nameof(value));
}
