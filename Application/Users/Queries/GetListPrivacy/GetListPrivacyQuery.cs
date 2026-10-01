using Application.Users.DTOS;
using MediatR;

namespace Application.Users.Queries.GetListPrivacy;

/// <summary>GET /api/User/me/privacy: who may browse the caller's review and comment lists (#61).</summary>
public class GetListPrivacyQuery : IRequest<ListPrivacyDto>
{
}
