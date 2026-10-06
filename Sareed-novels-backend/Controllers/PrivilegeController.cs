using Application.Privileges.Commands.CancelSubscription;
using Application.Privileges.Commands.DisablePrivilege;
using Application.Privileges.Commands.EnablePrivilege;
using Application.Privileges.Commands.ManualUnlock;
using Application.Privileges.Commands.Subscribe;
using Application.Privileges.Commands.UpdatePrivilege;
using Application.Privileges.Queries.GetMySubscriptions;
using Application.Privileges.Queries.GetNovelSubscribers;
using Application.Privileges.Queries.GetPrivilegeInfo;
using Application.Privileges.DTOs;
using Application.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers;

[ApiController]
[Route("api/novel/{novelId}/privilege")]
public class PrivilegeController(IMediator mediator) : ControllerBase
{
    // ===== READER ENDPOINTS =====
    
    /// <summary>
    /// A novel's early access, for anyone (#94): its settings (earlyAccessDays or subscribersOnly), its locked chapters
    /// now (lockedChaptersCount, nextUnlockAt, privilegeStartSequence), subscribersCount for its author, and the
    /// signed-in member's subscription, and the settings' rules (#96); <c>{ isEnabled: false, rules }</c> while it is off.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPrivilegeInfo([FromRoute] Guid novelId)
    {
        var query = new GetPrivilegeInfoQuery { NovelId = novelId };
        var result = await mediator.Send(query);
        
        if (result == null)
        {
            return Ok(new { isEnabled = false, rules = EarlyAccessRulesDto.Current });
        }
        
        return Ok(result);
    }

    /// <summary>
    /// The novel's subscribers, for its author only (#96), newest first: { subscribers: [{ userId, userName,
    /// displayName, profilePhoto, subscribedAt }], totalCount, pageNumber, pageSize, totalPages }, pages as
    /// GET /api/privilege/my-subscriptions. Also while early access is off (subscriptions stay). 403 NotOwner, 404
    /// NovelNotFound.
    /// </summary>
    [HttpGet("subscribers")]
    [Authorize]
    public async Task<IActionResult> GetSubscribers([FromRoute] Guid novelId, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
    {
        (pageNumber, pageSize) = Paging.Clamp(pageNumber, pageSize);
        var (subscribers, totalCount) = await mediator.Send(new GetNovelSubscribersQuery(novelId, pageNumber, pageSize));
        return Ok(new
        {
            subscribers,
            totalCount,
            pageNumber,
            pageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }
    
    /// <summary>
    /// Subscribe to a novel's privilege (authenticated users only)
    /// </summary>
    [HttpPost("subscribe")]
    [Authorize]
    public async Task<IActionResult> Subscribe([FromRoute] Guid novelId)
    {
        var command = new SubscribeToPrivilegeCommand { NovelId = novelId };
        var result = await mediator.Send(command);
        
        if (result.Success)
        {
            return Ok(result);
        }
        
        return BadRequest(result);
    }
    
    /// <summary>
    /// Subscriptions are permanent: a subscriber is refused, 400 SubscriptionCannotBeCancelled; without a subscription
    /// there is nothing to cancel, 204 (it was the same 400).
    /// </summary>
    [HttpDelete("subscription")]
    [Authorize]
    public async Task<IActionResult> CancelSubscription([FromRoute] Guid novelId) =>
        this.Answer(await mediator.Send(new CancelSubscriptionCommand { NovelId = novelId }));
    
    // ===== AUTHOR ENDPOINTS =====
    
    /// <summary>
    /// Turns early access on (#94), the first time or again after it was turned off: the published chapters from
    /// privilegeStartSequence on lock now (by default the last min(20, published - 10); never the first 10, at most 20),
    /// each for earlyAccessDays (1-30) or for subscribers only until the author frees it; neither sent is 7 days, both
    /// is 400 ValidationFailed, days out of range 400 InvalidEarlyAccessDays.
    /// </summary>
    [HttpPost("enable")]
    [Authorize]
    public async Task<IActionResult> EnablePrivilege(
        [FromRoute] Guid novelId,
        [FromBody] EnablePrivilegeRequest request)
    {
        var command = new EnablePrivilegeCommand
        {
            NovelId = novelId,
            SubscriptionCost = request.SubscriptionCost,
            PrivilegeStartSequence = request.PrivilegeStartSequence,
            EarlyAccessDays = request.EarlyAccessDays,
            SubscribersOnly = request.SubscribersOnly
        };
        
        var result = await mediator.Send(command);
        
        if (result.Success)
        {
            return Ok(result);
        }
        
        return BadRequest(result);
    }
    
    /// <summary>
    /// Changes early access (#94): the cost (new subscribers), the days or subscribers only (chapters that come out from
    /// now and those still locked, their end counted from their own lock; a freed chapter never locks again), and,
    /// for the website before #94, the first locked chapter, forward only. 400 NoChanges when nothing changes.
    /// </summary>
    [HttpPatch]
    [Authorize]
    public async Task<IActionResult> UpdatePrivilege(
        [FromRoute] Guid novelId,
        [FromBody] UpdatePrivilegeRequest request)
    {
        var command = new UpdatePrivilegeCommand
        {
            NovelId = novelId,
            NewSubscriptionCost = request.NewSubscriptionCost,
            NewPrivilegeStartSequence = request.NewPrivilegeStartSequence,
            EarlyAccessDays = request.EarlyAccessDays,
            SubscribersOnly = request.SubscribersOnly
        };
        
        var result = await mediator.Send(command);
        
        if (result.Success)
        {
            return Ok(result);
        }
        
        return BadRequest(result);
    }
    
    /// <summary>
    /// Frees that chapter only, for everyone, for good (#94). 400 ChapterNotLocked, NotOwner, PrivilegeNotEnabled or
    /// ChapterNotFound (also for a chapter of another novel).
    /// </summary>
    [HttpPost("manual-unlock/{chapterId}")]
    [Authorize]
    public async Task<IActionResult> ManualUnlockChapter(
        [FromRoute] Guid novelId,
        [FromRoute] Guid chapterId)
    {
        var command = new ManualUnlockChapterCommand { NovelId = novelId, ChapterId = chapterId };
        var result = await mediator.Send(command);
        
        if (result.Success)
        {
            return Ok(result);
        }
        
        return BadRequest(result);
    }

    /// <summary>
    /// Turns early access off (#94): every chapter opens to everyone and new chapters don't lock; subscriptions stay,
    /// for when it is turned on again (enable). 400 PrivilegeNotEnabled when it is off, NotOwner, NovelNotFound.
    /// </summary>
    [HttpPost("disable")]
    [Authorize]
    public async Task<IActionResult> DisablePrivilege([FromRoute] Guid novelId)
    {
        var result = await mediator.Send(new DisablePrivilegeCommand(novelId));
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

// ===== REQUEST MODELS =====

public class EnablePrivilegeRequest
{
    public decimal SubscriptionCost { get; set; }
    
    /// <summary>
    /// The published position of the first chapter to lock: 11 or after, at most 20 locked. Not sent: the last
    /// min(20, published - 10).
    /// </summary>
    public int? PrivilegeStartSequence { get; set; }

    /// <summary>How many days each locked chapter stays early (1-30, #94).</summary>
    public int? EarlyAccessDays { get; set; }

    /// <summary>Locked chapters stay locked for non-subscribers until the author frees them (#94).</summary>
    public bool? SubscribersOnly { get; set; }
}

public class UpdatePrivilegeRequest
{
    public decimal? NewSubscriptionCost { get; set; }
    
    /// <summary>
    /// The website before #94: moves the first locked chapter forward to this published position, freeing the locked
    /// chapters before it. Never back.
    /// </summary>
    public int? NewPrivilegeStartSequence { get; set; }

    /// <summary>New days (1-30, #94).</summary>
    public int? EarlyAccessDays { get; set; }

    /// <summary>True: subscribers only; false: back to days (#94).</summary>
    public bool? SubscribersOnly { get; set; }
}
