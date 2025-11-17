using Application.Services;
using Application.Users.Commands;
using Application.Users.Commands.ConfirmEmail;
using Application.Users.Commands.CreateUser;
using Application.Users.Commands.ForgotPassword;
using Application.Users.Commands.GoogleCallback;
using Application.Users.Commands.GoogleLogin;
using Application.Users.Commands.SendConfirmEmail;
using Application.Users.Commands.UserLogin;
using Domain.Constants;
using Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
namespace Sareed_novels_backend.Controllers
{
    [ApiController]
    [Route("api/identity")]
    public class IdentityController(IMediator mediator, IConfiguration configuration) : ControllerBase
    {
        [HttpPost("Register")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> CreateUser(CreateUserCommand command)
        {
            var result = await mediator.Send(command);
            if (!result.Result.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }

        [HttpPost("Confirm-email")]
        public async Task<IActionResult> ConfirmEmail(ConfirmEmailCommand command)
        {
            var result = await mediator.Send(command);
            if (result.Succeeded == false)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }

        [HttpPost("Send-Email")]
        public async Task<IActionResult> SendConfirmationLink(SendConfirmEmailCommand command)
        {
            await mediator.Send(command);
            return Ok("Email was sent.");
        }

        [HttpPost("Login")]
        public async Task<IActionResult> Login(UserLoginCommand command)
        {
            var response = await mediator.Send(command);
            return Ok(response);
        }

        [HttpPost("google-login")]
        [Obsolete("Use Authorization Code Flow via /google-callback instead")]
        public async Task<IActionResult> GoogleLogin(GoogleLoginCommand command)
        {
            var response = await mediator.Send(command);
            return Ok(response);
        }

        [HttpGet("google-callback")]
        public async Task<IActionResult> GoogleCallback(string code, string? state, string? error)
        {
            var frontendUrl = configuration["Frontend:Url"] ?? "https://www.sardnovels.com";

            if (!string.IsNullOrEmpty(error))
            {
                return Redirect($"{frontendUrl}/auth/error?error={error}");
            }

            if (string.IsNullOrEmpty(code))
            {
                return Redirect($"{frontendUrl}/auth/error?error=no_code");
            }

            try
            {
                var command = new GoogleCallbackCommand { Code = code, State = state };
                var result = await mediator.Send(command);
                return Redirect($"{frontendUrl}/auth/success?token={result.AccessToken}");
            }
            catch (Exception)
            {
                return Redirect($"{frontendUrl}/auth/error?error=auth_failed");
            }
        }

        [HttpPost("forget-password")]
        public async Task<IActionResult> ForgetPassword(ForgotPasswordCommand command)
        {
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword(ResetPasswordCommand command)
        {
            var result = await mediator.Send(command);
            if (!result.Succeeded)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }

        // ⚠️ DEVELOPMENT ONLY - Remove before production!
        [HttpPost("make-admin")]
        [Authorize]
        public async Task<IActionResult> MakeCurrentUserAdmin(
            [FromServices] UserManager<User> userManager,
            [FromServices] IHttpContextAccessor contextAccessor)
        {
            var userId = contextAccessor.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User not authenticated");
            }

            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound("User not found");
            }

            // Check if already admin
            if (await userManager.IsInRoleAsync(user, UserRoles.Admin))
            {
                return Ok(new { message = $"User {user.DisplayName} is already an Admin", isAdmin = true });
            }

            var result = await userManager.AddToRoleAsync(user, UserRoles.Admin);
            if (result.Succeeded)
            {
                return Ok(new { message = $"User {user.DisplayName} is now an Admin!", isAdmin = true });
            }

            return BadRequest(new { message = "Failed to assign admin role", errors = result.Errors });
        }
    }


}
