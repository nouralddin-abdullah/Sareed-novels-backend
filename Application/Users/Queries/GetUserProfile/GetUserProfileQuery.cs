using Application.Users.DTOS;
using MediatR;

namespace Application.Users.Queries.GetUserProfile;

public class GetUserProfileQuery : IRequest<UserProfile>
{
    public string UserName { get; set; } = default!;

}
