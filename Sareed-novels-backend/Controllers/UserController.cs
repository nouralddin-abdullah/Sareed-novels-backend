using Application.Users.Commands.BlockUser;
using Application.Users.Commands.ChangePassword;
using Application.Users.Commands.DeleteAccount;
using Application.Users.Commands.FollowUser;
using Application.Users.Commands.SetPassword;
using Application.Users.Commands.UnblockUser;
using Application.Users.Commands.UnFollowUser;
using Application.Users.Commands.UpdateListPrivacy;
using Application.Users.Commands.UpdateMe;
using Application.Users.Queries.GetBlockedUsers;
using Application.Users.Queries.GetFollowersList;
using Application.Users.Queries.GetFollowingList;
using Application.Users.Queries.GetListPrivacy;
using Application.Users.Queries.GetMyProfile;
using Application.Users.Queries.GetUserComments;
using Application.Users.Queries.GetUserProfile;
using Application.Users.Queries.GetUserReviews;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Sareed_novels_backend.Extensions;

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

        /// <summary>
        /// Edits the caller's profile (the text fields in the query string or the form, the pictures in the form). 200
        /// {success, message, profile}: profile is exactly what GET my-profile returns, read after the save (#44). A
        /// refusal answers as before, without profile: 400 {success: false, code, message} (UploadFailed adds field), or
        /// the validation problem (ValidationFailed).
        /// </summary>
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

        /// <summary>
        /// Gives an account without a password (made with Google) its first one (#53), from
        /// {newPassword, googleIdToken?}: 204, and every session stays signed in. Refusals in the order they are
        /// checked: 400 PasswordAlreadySet (update-password changes it); 400 for a password the rules refuse, with
        /// update-password's code and message (ValidationFailed, or Identity's code in IdentityErrors.Body); 403
        /// ReauthenticationRequired or ReauthenticationFailed, the proof DELETE me takes from an account without a
        /// password (a Google ID token of its Google sign-in, or a sign-in in the last 10 minutes). 10 requests a
        /// minute per address, like sign-in.
        /// </summary>
        [HttpPost("set-password")]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        public async Task<IActionResult> SetPassword(SetPasswordRequest request)
        {
            var result = await mediator.Send(new SetPasswordCommand(request.NewPassword, request.GoogleIdToken));
            if (!result.Succeeded)
            {
                return BadRequest(IdentityErrors.Body(result));
            }
            return NoContent();
        }

        /// <summary>Follows a user; 204 when the caller already does (it was 400 AlreadyFollowing); 400 CannotFollowSelf.</summary>
        [HttpPost("follow")]
        public async Task<IActionResult> FollowUser(FollowUserCommand command) => this.Answer(await mediator.Send(command));

        /// <summary>Unfollows a user; 204 when the caller doesn't follow them (it was 400 NotFollowing); 400 CannotUnfollowSelf.</summary>
        [HttpDelete("unfollow")]
        public async Task<IActionResult> FollowUser(UnFollowUserCommand command) => this.Answer(await mediator.Send(command));

        /// <summary>
        /// Blocks a user (idempotent): their comments, replies, reviews and posts leave the caller's lists, follows
        /// between the two are removed, and they can't open the caller's profile, lists or posts. Neither of the two
        /// can then follow the other, like the other's content, comment on the other's posts or reply to the other's
        /// comments, and no notification passes between them but for a gift or a subscription from the caller (README,
        /// #52).
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

        /// <summary>
        /// Who may browse the caller's review and comment lists (#61, README): {"reviews", "comments"}, each "Everyone"
        /// (the default) or "OnlyMe".
        /// </summary>
        [HttpGet("me/privacy")]
        public async Task<IActionResult> GetListPrivacy(CancellationToken cancellationToken) =>
            Ok(await mediator.Send(new GetListPrivacyQuery(), cancellationToken));

        /// <summary>
        /// Sets who may browse the caller's review and comment lists (#61, README) from {"reviews"?, "comments"?}: each
        /// "Everyone" or "OnlyMe" in any letter case; one left out (or null) keeps its setting, and an empty body (or
        /// {}) changes nothing. Answers both settings as GET me/privacy does. 400 ValidationFailed for any other value.
        /// </summary>
        [HttpPatch("me/privacy")]
        public async Task<IActionResult> UpdateListPrivacy(
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpdateListPrivacyCommand? command,
            CancellationToken cancellationToken) =>
            Ok(await mediator.Send(command ?? new UpdateListPrivacyCommand(), cancellationToken));

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

        /// <summary>
        /// The member's reviews on novels readers can open, newest first (#54, README): pageNumber from 1, pageSize 1
        /// to 50 (10 by default). Its total is reviewsCount on the profile. 404 UserNotFound as GET {userName} (an old
        /// user name finds the member too); then 403 ListHidden to anyone but the member when they hid it (#61); then
        /// an empty page when the signed-in viewer and the member blocked each other, either way.
        /// </summary>
        [HttpGet("{userName}/reviews")]
        [AllowAnonymous]
        public async Task<IActionResult> GetUserReviews([FromRoute] string userName, [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
            Ok(await mediator.Send(new GetUserReviewsQuery(userName, pageNumber ?? 1, pageSize ?? 10), cancellationToken));

        /// <summary>
        /// The member's comments and replies on chapters and paragraphs readers can open, newest first (#54, README),
        /// paged and answered like GET {userName}/reviews. Its total is commentsCount on the profile. Comments on posts
        /// aren't listed. A reply carries the comment it answers, and a paragraph comment the start of its paragraph
        /// when the viewer may read the chapter (#60).
        /// </summary>
        [HttpGet("{userName}/comments")]
        [AllowAnonymous]
        public async Task<IActionResult> GetUserComments([FromRoute] string userName, [FromQuery] int? pageNumber,
            [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
            Ok(await mediator.Send(new GetUserCommentsQuery(userName, pageNumber ?? 1, pageSize ?? 10), cancellationToken));
    }
}
