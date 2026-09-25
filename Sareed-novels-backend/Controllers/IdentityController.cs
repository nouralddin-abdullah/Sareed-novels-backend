using Application.Services;
using Application.Users.Commands;
using Application.Users.Commands.ConfirmEmail;
using Application.Users.Commands.CreateUser;
using Application.Users.Commands.ForgotPassword;
using Application.Users.Commands.GoogleCallback;
using Application.Users.Commands.GoogleLogin;
using Application.Users.Commands.SendConfirmEmail;
using Application.Users.Commands.UserLogin;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sareed_novels_backend.Extensions;
namespace Sareed_novels_backend.Controllers
{
    [ApiController]
    [Route("api/identity")]
    public class IdentityController(IMediator mediator, IConfiguration configuration) : ControllerBase
    {
        [HttpPost("Register")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
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
        [EnableRateLimiting(RateLimitPolicies.Auth)]
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
        [EnableRateLimiting(RateLimitPolicies.Email)]
        public async Task<IActionResult> SendConfirmationLink(SendConfirmEmailCommand command)
        {
            await mediator.Send(command);
            return Ok("Email was sent.");
        }

        [HttpPost("Login")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public async Task<IActionResult> Login(UserLoginCommand command)
        {
            var response = await mediator.Send(command);
            return Ok(response);
        }

        [HttpPost("google-login")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        [Obsolete("Use Authorization Code Flow via /google-callback instead")]
        public async Task<IActionResult> GoogleLogin(GoogleLoginCommand command)
        {
            var response = await mediator.Send(command);
            return Ok(response);
        }

        [HttpGet("google-callback")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public async Task<IActionResult> GoogleCallback(string code, string? state, string? error)
        {
            var frontendUrl = configuration["Frontend:Url"] ?? "https://www.sardnovels.com";

            if (!string.IsNullOrEmpty(error))
            {
                return Redirect($"{frontendUrl}/auth/error?error={Uri.EscapeDataString(error)}");
            }

            if (string.IsNullOrEmpty(code))
            {
                return Redirect($"{frontendUrl}/auth/error?error=no_code");
            }

            try
            {
                var command = new GoogleCallbackCommand { Code = code, State = state };
                var result = await mediator.Send(command);
                // The token goes in the URL fragment, which browsers never send to servers, so it stays
                // out of access logs, proxies and Referer headers. The web app reads it and strips it.
                return Redirect($"{frontendUrl}/auth/success#token={Uri.EscapeDataString(result.AccessToken)}");
            }
            catch (Exception)
            {
                return Redirect($"{frontendUrl}/auth/error?error=auth_failed");
            }
        }

        [HttpPost("forget-password")]
        [EnableRateLimiting(RateLimitPolicies.Email)]
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
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public async Task<IActionResult> ResetPassword(ResetPasswordCommand command)
        {
            var result = await mediator.Send(command);
            if (!result.Succeeded)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }
    }


}
