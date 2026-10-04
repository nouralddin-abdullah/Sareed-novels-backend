using Application.Users.DTOS;
using Domain.Entities;
using Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace Application.Users.Queries.GetUserNameAvailability;

public class GetUserNameAvailabilityQueryHandler(
    IUserContext userContext,
    UserManager<User> userManager,
    UserNameCheck userNameCheck) : IRequestHandler<GetUserNameAvailabilityQuery, UserNameAvailabilityDto>
{
    public async Task<UserNameAvailabilityDto> Handle(GetUserNameAvailabilityQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        // The account as saved, as update-me reads it: the token may still carry a name changed since.
        var member = await userManager.FindByIdAsync(currentUser.Id) ?? throw ProfileLookup.NotFound();
        return UserNameAvailabilityDto.From(await userNameCheck.RefusalAsync(request.UserName, member));
    }
}
