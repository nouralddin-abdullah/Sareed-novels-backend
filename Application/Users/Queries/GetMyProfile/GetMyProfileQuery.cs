using Application.Users.DTOS;
using MediatR;

namespace Application.Users.Queries.GetMyProfile;

public class GetMyProfileQuery : IRequest<UserIsProfile>
{
}
