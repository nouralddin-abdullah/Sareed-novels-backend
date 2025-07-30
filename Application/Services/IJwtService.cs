using System.Security.Claims;
using Domain.Entities;

namespace Application.Services;

public interface IJWTService
{
    string GenerateAccessToken(User user);

}
