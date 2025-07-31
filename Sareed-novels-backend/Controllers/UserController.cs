using Application.Users.Commands.ChangePassword;
using Application.Users.Commands.FollowUser;
using Application.Users.Commands.UnFollowUser;
using Application.Users.Commands.UpdateMe;
using Application.Users.Queries.GetFollowersList;
using Application.Users.Queries.GetFollowingList;
using Application.Users.Queries.GetMyProfile;
using Application.Users.Queries.GetUserProfile;
using Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers
{
    [ApiController]
    [Route("api/User")]
    [Authorize]
    public class UserController(IMediator mediator) : ControllerBase
    {
        [HttpGet("my-profile")]
        public async Task<IActionResult> GetMyProfile()
        {
            var result = await mediator.Send(new GetMyProfileQuery());
            return Ok(result);
        }

        [HttpPatch("update-me")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UpdateMe(UpdateMeCommand command)
        {
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result.Message);
            }
            return Ok(result);
        }

        [HttpPatch("update-password")]
        public async Task<IActionResult> UpdatePassword(ChangePasswordCommand command)
        {
            var result = await mediator.Send(command);
            if (!result.Succeeded)
            {
                return BadRequest(result.Errors);
            }
            return Ok(result);
        }

        [HttpPost("follow")]
        public async Task<IActionResult> FollowUser(FollowUserCommand command)
        {
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result.Message);
            }

            return Ok(result);
        }

        [HttpDelete("unfollow")]
        public async Task<IActionResult> FollowUser(UnFollowUserCommand command)
        {
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result.Message);
            }

            return Ok(result);
        }

        [HttpGet("followers-list/{userId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetFollowersList([FromRoute] string userId,[FromQuery] GetFollowersListQueryRequest request)
        {
            var query = new GetFollowersListQuery(userId, request.PageSize, request.PageNumber);
            var paginatedFollowersList = await mediator.Send(query);
            return Ok(paginatedFollowersList);
        }

        [HttpGet("following-list/{userId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetFollowingList([FromRoute] string userId, [FromQuery] GetFollowingListQueryRequest request)
        {
            var query = new GetFollowingListQuery(userId, request.PageSize, request.PageNumber);
            var paginatedFollowingList = await mediator.Send(query);
            return Ok(paginatedFollowingList);
        }

        [HttpGet("{userName}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetUser([FromRoute] string userName)
        {
            var query = new GetUserProfileQuery(userName);
            var userDto = await mediator.Send(query);
            return Ok(userDto);
        }
    }
}
