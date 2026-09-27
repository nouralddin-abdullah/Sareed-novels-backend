using Application.Notifications.Commands.MarkAllAsRead;
using Application.Notifications.Commands.MarkAsRead;
using Application.Notifications.Commands.RegisterDevice;
using Application.Notifications.Commands.UnregisterDevice;
using Application.Notifications.Commands.UpdatePreferences;
using Application.Notifications.Queries.GetComment;
using Application.Notifications.Queries.GetNotifications;
using Application.Notifications.Queries.GetPreferences;
using Application.Notifications.Queries.GetUnreadCount;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sareed_novels_backend.Extensions;

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

    /// <summary>Registers the app's FCM token for push notifications (after sign-in and on token refresh).</summary>
    [HttpPost("devices")]
    [EnableRateLimiting(RateLimitPolicies.Devices)]
    public async Task<IActionResult> RegisterDevice([FromBody] RegisterDeviceCommand command)
    {
        await mediator.Send(command);
        return NoContent();
    }

    /// <summary>Unregisters the caller's FCM token (on sign-out). The token must be URL-encoded.</summary>
    [HttpDelete("devices/{token}")]
    public async Task<IActionResult> UnregisterDevice([FromRoute] string token)
    {
        await mediator.Send(new UnregisterDeviceCommand(token));
        return NoContent();
    }

    /// <summary>Which notification groups (social, chapters, support) are sent as push notifications.</summary>
    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences()
    {
        var result = await mediator.Send(new GetNotificationPreferencesQuery());
        return Ok(result);
    }

    /// <summary>Switches push notification groups on or off; groups left out keep their setting.</summary>
    [HttpPatch("preferences")]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdateNotificationPreferencesCommand command)
    {
        var result = await mediator.Send(command);
        return Ok(result);
    }
}
