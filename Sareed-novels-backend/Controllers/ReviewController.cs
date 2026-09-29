using Application.Reviews.Commands.CreateLikeReview;
using Application.Reviews.Commands.CreateReview;
using Application.Reviews.Commands.DeleteReview;
using Application.Reviews.Commands.DeleteReviewLike;
using Application.Reviews.Commands.UpdateReview;
using Application.Reviews.Queries.GetNovelReviews;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSwag.Annotations;

namespace Sareed_novels_backend.Controllers
{
    [ApiController]
    [Route("/api/{novelId}")]
    [Authorize]
    public class ReviewController(IMediator mediator) : ControllerBase
    {
        [HttpPost]
        public async Task<IActionResult> CreateReview([FromRoute] Guid novelId, [FromBody] CreateReviewCommandrRequest request)
        {
            var command = new CreateReviewCommand(novelId, request.WritingQualityScore, request.UpdatingStabilityScore, request.CharacterDevelopmentScore, request.WorldBuildingScore, request.IsSpoiler, request.Content!);
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }
        [HttpDelete]
        public async Task<IActionResult> CreateReview([FromRoute] Guid novelId)
        {
            var command = new DeleteReviewCommand(novelId);
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return NoContent();
        }
        /// <summary>
        /// Its author edits a review (#34). Every field of the body is optional; one left out stays as it is. 200 with
        /// the review itself, exactly an item of GET /api/{novelId} (id, likes and createdAt kept, updatedAt set), to
        /// replace it in place; 400 ValidationFailed as when writing one; 403 NotOwner; 404 ReviewNotFound.
        /// </summary>
        [HttpPatch("reviews/{reviewId}")]
        public async Task<IActionResult> UpdateReview([FromRoute] Guid novelId, [FromRoute] Guid reviewId, [FromBody] UpdateReviewRequest request) =>
            Ok(await mediator.Send(new UpdateReviewCommand(novelId, reviewId, request)));

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetNovelReviews([FromRoute] Guid novelId, [FromQuery] int? pageSize, [FromQuery] int? pageNumber, [FromQuery] string? sorting)
        {
            var query = new GetNovelReviewsQuery(novelId, pageSize ?? 10 , pageNumber ?? 1, sorting ?? "newest");
            var result = await mediator.Send(query);
            return Ok(result);
        }

        /// <summary>204 when the caller already likes it (it was 400 AlreadyLiked).</summary>
        [HttpPost("reviews/{reviewId}/like")]
        public async Task<IActionResult> LikeReview([FromRoute] Guid reviewId) =>
            this.Answer(await mediator.Send(new CreateLikeReviewCommand(reviewId)));

        /// <summary>204 when the caller doesn't like it (it was 400 NotLiked).</summary>
        [HttpDelete("reviews/{reviewId}/unlike")]
        public async Task<IActionResult> UnLikeReview([FromRoute] Guid reviewId) =>
            this.Answer(await mediator.Send(new DeleteReviewLikeCommand(reviewId)));
    }
}
