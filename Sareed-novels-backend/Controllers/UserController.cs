using Application.Users.Commands.BlockUser;
using Application.Users.Commands.ChangePassword;
using Application.Users.Commands.FollowUser;
using Application.Users.Commands.UnblockUser;
using Application.Users.Commands.UnFollowUser;
using Application.Users.Commands.UpdateMe;
using Application.Users.Queries.GetBlockedUsers;
using Application.Users.Queries.GetFollowersList;
using Application.Users.Queries.GetFollowingList;
using Application.Users.Queries.GetMyProfile;
using Application.Users.Queries.GetUserProfile;
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
                return BadRequest(result);
            }
            return Ok(result);
        }

        /// <summary>
        /// Changes the caller's password. A refusal is 400 {code, message, errors}: code is ASP.NET Identity's for the first
        /// problem (PasswordMismatch for a wrong current password, PasswordTooShort...), message its Arabic description.
        /// </summary>
        [HttpPatch("update-password")]
        public async Task<IActionResult> UpdatePassword(ChangePasswordCommand command)
        {
            var result = await mediator.Send(command);
            if (!result.Succeeded)
            {
                return BadRequest(IdentityErrors.Body(result));
            }
            return Ok(result);
        }

        /// <summary>Follows a user; 400 AlreadyFollowing when the caller already does (and CannotFollowSelf).</summary>
        [HttpPost("follow")]
        public async Task<IActionResult> FollowUser(FollowUserCommand command)
        {
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }

        /// <summary>Unfollows a user; 400 NotFollowing when the caller doesn't follow them (and CannotUnfollowSelf).</summary>
        [HttpDelete("unfollow")]
        public async Task<IActionResult> FollowUser(UnFollowUserCommand command)
        {
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }

        /// <summary>
        /// Blocks a user (idempotent): their comments, replies, reviews and posts leave the caller's lists, follows
        /// between the two are removed, and they can't follow, answer, notify or open the caller.
        /// </summary>
        [HttpPost("block")]
        public async Task<IActionResult> BlockUser(BlockUserCommand command) => Ok(await mediator.Send(command));

        /// <summary>Unblocks a user (idempotent).</summary>
        [HttpDelete("unblock")]
        public async Task<IActionResult> UnblockUser(UnblockUserCommand command) => Ok(await mediator.Send(command));

        /// <summary>The same as DELETE unblock, for clients that can't send a body with DELETE.</summary>
        [HttpDelete("block/{userId}")]
        public async Task<IActionResult> UnblockUserById([FromRoute] string userId) =>
            Ok(await mediator.Send(new UnblockUserCommand { UserId = userId }));

        /// <summary>The users the caller blocked, most recent first, with their current names.</summary>
        [HttpGet("blocked")]
        public async Task<IActionResult> GetBlockedUsers([FromQuery] int? pageNumber, [FromQuery] int? pageSize) =>
            Ok(await mediator.Send(new GetBlockedUsersQuery(pageNumber ?? 1, pageSize ?? 20)));

        [HttpGet("followers-list/{userId}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetFollowersList([FromRoute] string userId, [FromQuery] GetFollowersListQueryRequest request)
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
