using System.Security.Claims;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using static Infrastructure.Extensions.Constants;

namespace Infrastructure.Authorization;

public class SardUserClaimsPrincipalFactory(UserManager<User> userManager, RoleManager<IdentityRole> roleManager, IOptions<IdentityOptions> options) 
    : UserClaimsPrincipalFactory<User, IdentityRole>(userManager, roleManager, options)
{
    public override async Task<ClaimsPrincipal> CreateAsync(User user)
    {
        var id = await GenerateClaimsAsync(user);
        id.AddClaim(new Claim(AppClaimTypes.DisplayName, user.DisplayName));

        return new ClaimsPrincipal(id);
    }
}
