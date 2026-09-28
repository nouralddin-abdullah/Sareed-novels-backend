using Application.Users.Commands.BlockUser;
using Application.Users.Commands.ChangePassword;
using Application.Users.Commands.DeleteAccount;
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
using Microsoft.AspNetCore.Mvc.ModelBinding;

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

        /// <summary>
        /// Deletes the signed-in member's account for good (IAccountDeletionService): 204 when done; every token of the
        /// account is refused from then on (401). The body confirms it's them: {"password"} for an account with a
        /// password, {"googleIdToken"} from a fresh Google sign-in, or nothing for an account without a password whose
        /// token comes from a sign-in in the last 10 minutes. 403 ReauthenticationFailed, ReauthenticationRequired or
        /// AdminCannotDeleteAccount; 429 TooManyDeletionAttempts (5 attempts an hour per account).
        /// </summary>
        [HttpDelete("me")]
        public async Task<IActionResult> DeleteMe([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeleteAccountCommand? command)
        {
            await mediator.Send(command ?? new DeleteAccountCommand());
            return NoContent();
        }

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
