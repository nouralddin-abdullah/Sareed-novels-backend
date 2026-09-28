using Application.Users.Commands.FollowUser;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers;

/// <summary>How the idempotent writes (follow, like, list follow...) answer their <see cref="OperationResult"/>.</summary>
internal static class OperationResults
{
    /// <summary>
    /// 200 with the result when the request changed something; 204 No Content when the state was already as asked
    /// (<see cref="OperationResult.Unchanged"/>: already liked, not following...), so clients can treat a repeat as
    /// done (#25); 400 with the result (its code and Arabic message) when it was refused.
    /// </summary>
    public static IActionResult Answer(this ControllerBase controller, OperationResult result) =>
        result.Unchanged ? controller.NoContent()
        : result.Success ? controller.Ok(result)
        : controller.BadRequest(result);
}
