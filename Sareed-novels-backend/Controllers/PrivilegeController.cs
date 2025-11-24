using Application.Privileges.Commands.CancelSubscription;
using Application.Privileges.Commands.EnablePrivilege;
using Application.Privileges.Commands.ManualUnlock;
using Application.Privileges.Commands.Subscribe;
using Application.Privileges.Commands.UpdatePrivilege;
using Application.Privileges.Queries.GetMySubscriptions;
using Application.Privileges.Queries.GetPrivilegeInfo;
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
    /// Get privilege information for a novel (visible to all users)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPrivilegeInfo([FromRoute] Guid novelId)
    {
        var query = new GetPrivilegeInfoQuery { NovelId = novelId };
        var result = await mediator.Send(query);
        
        if (result == null)
        {
            return Ok(new { isEnabled = false });
        }
        
        return Ok(result);
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
    /// Cancel privilege subscription (authenticated users only)
    /// </summary>
    [HttpDelete("subscription")]
    [Authorize]
    public async Task<IActionResult> CancelSubscription([FromRoute] Guid novelId)
    {
        var command = new CancelSubscriptionCommand { NovelId = novelId };
        var result = await mediator.Send(command);
        
        if (result.Success)
        {
            return Ok(result);
        }
        
        return BadRequest(result);
    }
    
    // ===== AUTHOR ENDPOINTS =====
    
    /// <summary>
    /// Enable privilege system for a novel (author only)
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
            PrivilegeStartSequence = request.PrivilegeStartSequence
        };
        
        var result = await mediator.Send(command);
        
        if (result.Success)
        {
            return Ok(result);
        }
        
        return BadRequest(result);
    }
    
    /// <summary>
    /// Update privilege configuration (author only)
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
            NewPrivilegeStartSequence = request.NewPrivilegeStartSequence
        };
        
        var result = await mediator.Send(command);
        
        if (result.Success)
        {
            return Ok(result);
        }
        
        return BadRequest(result);
    }
    
    /// <summary>
    /// Manually unlock a privilege chapter (author only)
    /// </summary>
    [HttpPost("manual-unlock/{chapterId}")]
    [Authorize]
    public async Task<IActionResult> ManualUnlockChapter(
        [FromRoute] Guid novelId,
        [FromRoute] Guid chapterId)
    {
        var command = new ManualUnlockChapterCommand { ChapterId = chapterId };
        var result = await mediator.Send(command);
        
        if (result.Success)
        {
            return Ok(result);
        }
        
        return BadRequest(result);
    }
}

// ===== REQUEST MODELS =====

public class EnablePrivilegeRequest
{
    public decimal SubscriptionCost { get; set; }
    
    /// <summary>
    /// Optional: The PublishedChapterSequence from which privilege should start.
    /// Example: If you have 50 published chapters and set this to 31,
    /// chapters 31-50 will be locked (20 chapters).
    /// If not provided, the last 20 published chapters will be locked.
    /// </summary>
    public int? PrivilegeStartSequence { get; set; }
}

public class UpdatePrivilegeRequest
{
    public decimal? NewSubscriptionCost { get; set; }
    
    /// <summary>
    /// Optional: Move privilege start forward (unlock more chapters).
    /// Can only move FORWARD, not backward.
    /// Example: Current start is 31, move to 40 = unlock chapters 31-39.
    /// </summary>
    public int? NewPrivilegeStartSequence { get; set; }
}
