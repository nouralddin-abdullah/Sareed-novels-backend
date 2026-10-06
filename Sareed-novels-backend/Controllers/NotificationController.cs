using Application.Notifications.Commands.MarkAllAsRead;
using Application.Notifications.Commands.MarkAsRead;
using Application.Notifications.Commands.MarkTypesAsRead;
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
    /// <summary>
    /// The caller's notifications, newest first. <paramref name="types"/> (#78): only these NotificationType names,
    /// comma-separated, in any letter case; unknown names are ignored, and none known (or none at all) means every type.
    /// With it, totalCount and unreadCount count those types only.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetNotifications([FromQuery] int? pageNumber, [FromQuery] int? pageSize, [FromQuery] bool? unreadOnly,
        [FromQuery] string[]? types)
    {
        var query = new GetNotificationsQuery(pageNumber ?? 1, pageSize ?? 20, unreadOnly ?? false, types);
        var result = await mediator.Send(query);
        return Ok(result);
    }

    /// <summary>How many of the caller's notifications are unread; with <paramref name="types"/>, of those types only (#78).</summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount([FromQuery] string[]? types)
    {
        var query = new GetUnreadCountQuery(types);
        var count = await mediator.Send(query);
        return Ok(new { unreadCount = count });
    }

    [HttpGet("comment/{commentId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetComment([FromRoute] Guid commentId, [FromQuery] int? pageSize)
    {
        // pageSize: what the client pages the comment's list with, so context.pageNumber lands on the right page.
        var query = new GetCommentQuery(commentId, pageSize ?? 10);
        var result = await mediator.Send(query);
        return Ok(result);
    }

    /// <summary>Marks one of the caller's notifications read; 204 again when it already is.</summary>
    [HttpPatch("{notificationId}/read")]
    public async Task<IActionResult> MarkAsRead([FromRoute] Guid notificationId)
    {
        await mediator.Send(new MarkNotificationAsReadCommand(notificationId));
        return NoContent();
    }

    /// <summary>Marks all of the caller's notifications read: 204, also when none was unread.</summary>
    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        await mediator.Send(new MarkAllNotificationsAsReadCommand());
        return NoContent();
    }

    /// <summary>
    /// Marks the caller's unread notifications of <paramref name="types"/> read, and no others (#92, «على رواياتي»):
    /// types as GET /api/notifications takes them, except that naming no known type marks nothing (never every type;
    /// that is read-all). Answers 200 { marked, unreadCount, typesUnreadCount }: how many it marked, and the unread
    /// counts after it, of every type and of these types. 400 ValidationFailed when types names nothing at all.
    /// </summary>
    [HttpPatch("read")]
    public async Task<IActionResult> MarkTypesAsRead([FromQuery] string[]? types)
    {
        var result = await mediator.Send(new MarkTypesAsReadCommand(types));
        return Ok(result);
    }

    /// <summary>Registers the app's FCM token for push notifications (after sign-in and on token refresh).</summary>
    [HttpPost("devices")]
    [EnableRateLimiting(RateLimitPolicies.Devices)]
    public async Task<IActionResult> RegisterDevice([FromBody] RegisterDeviceCommand command)
    {
        await mediator.Send(command);
        return NoContent();
    }

    /// <summary>
    /// Unregisters an FCM token (on sign-out); the token must be URL-encoded. Works without a valid session too (the
    /// app signed out after a 401, or offline): then the token is removed whoever registered it, as knowing it is the
    /// proof. With one, only the caller's own registration is removed. Always 204; rate-limited per IP.
    /// </summary>
    [HttpDelete("devices/{token}")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Devices)]
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
