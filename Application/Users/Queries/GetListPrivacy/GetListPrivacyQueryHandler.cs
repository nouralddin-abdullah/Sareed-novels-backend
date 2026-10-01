using Application.Users.DTOS;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Users.Queries.GetListPrivacy;

public class GetListPrivacyQueryHandler(
    IUsersRepository usersRepository,
    IUserContext userContext) : IRequestHandler<GetListPrivacyQuery, ListPrivacyDto>
{
    public async Task<ListPrivacyDto> Handle(GetListPrivacyQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var privacy = await usersRepository.GetListPrivacyAsync(currentUser.Id, cancellationToken)
            ?? throw ProfileLookup.NotFound();
        return ListPrivacyDto.From(privacy);
    }
}
