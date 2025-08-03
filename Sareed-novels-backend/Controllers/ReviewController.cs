using Application.Reviews.Commands.CreateLikeReview;
using Application.Reviews.Commands.CreateReview;
using Application.Reviews.Commands.DeleteReview;
using Application.Reviews.Commands.DeleteReviewLike;
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
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetNovelReviews([FromRoute] Guid novelId, [FromQuery] int? pageSize, [FromQuery] int? pageNumber, [FromQuery] string? sorting)
        {
            var query = new GetNovelReviewsQuery(novelId, pageSize ?? 10 , pageNumber ?? 1, sorting ?? "newest");
            var result = await mediator.Send(query);
            return Ok(result);
        }

        [HttpPost("reviews/{reviewId}/like")]
        public async Task<IActionResult> LikeReview([FromRoute] Guid reviewId)
        {
            var command = new CreateLikeReviewCommand(reviewId);
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }

        [HttpDelete("reviews/{reviewId}/unlike")]
        public async Task<IActionResult> UnLikeReview([FromRoute] Guid reviewId)
        {
            var command = new DeleteReviewLikeCommand(reviewId);
            var result = await mediator.Send(command);
            if (!result.Success)
            {
                return BadRequest(result);
            }
            return Ok(result);
        }
    }
}
