using Application.Notifications.Commands.MarkAllAsRead;
using Application.Notifications.Commands.MarkAsRead;
using Application.Notifications.Queries.GetComment;
using Application.Notifications.Queries.GetNotifications;
using Application.Notifications.Queries.GetUnreadCount;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetNotifications([FromQuery] int? pageNumber, [FromQuery] int? pageSize, [FromQuery] bool? unreadOnly)
    {
        var query = new GetNotificationsQuery(pageNumber ?? 1, pageSize ?? 20, unreadOnly ?? false);
        var result = await mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var query = new GetUnreadCountQuery();
        var count = await mediator.Send(query);
        return Ok(new { unreadCount = count });
    }

    [HttpGet("comment/{commentId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetComment([FromRoute] Guid commentId)
    {
        var query = new GetCommentQuery(commentId);
        var result = await mediator.Send(query);
        return Ok(result);
    }

    [HttpPatch("{notificationId}/read")]
    public async Task<IActionResult> MarkAsRead([FromRoute] Guid notificationId)
    {
        var command = new MarkNotificationAsReadCommand(notificationId);
        var result = await mediator.Send(command);
        if (result)
        {
            return NoContent();
        }
        return BadRequest("Failed to mark notification as read");
    }

    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var command = new MarkAllNotificationsAsReadCommand();
        var result = await mediator.Send(command);
        if (result)
        {
            return NoContent();
        }
        return BadRequest("Failed to mark all notifications as read");
    }
}
