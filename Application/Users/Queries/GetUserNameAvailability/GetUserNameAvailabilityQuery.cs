using Application.Users.DTOS;
using MediatR;

namespace Application.Users.Queries.GetUserNameAvailability;

/// <summary>
/// GET /api/User/username-available?userName=: whether the caller could take <paramref name="UserName"/> as their user
/// name now (#69, README). Choosing it is still PATCH update-me.
/// </summary>
public record GetUserNameAvailabilityQuery(string? UserName) : IRequest<UserNameAvailabilityDto>;
