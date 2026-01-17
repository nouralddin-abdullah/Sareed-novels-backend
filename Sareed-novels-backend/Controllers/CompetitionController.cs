using Application.Common;
using Application.Competitions.Commands.AddExtraPoints;
using Application.Competitions.Commands.CreateCompetition;
using Application.Competitions.Commands.FinalizeCompetition;
using Application.Competitions.Commands.JoinCompetition;
using Application.Competitions.Commands.LeaveCompetition;
using Application.Competitions.Commands.RecalculateRankings;
using Application.Competitions.Commands.UpdateCompetition;
using Application.Competitions.DTOs;
using Application.Competitions.Queries.GetCompetitionDetail;
using Application.Competitions.Queries.GetCompetitionLeaderboard;
using Application.Competitions.Queries.GetCompetitionNovels;
using Application.Competitions.Queries.GetCompetitions;
using Application.Competitions.Queries.GetMyEligibleNovels;
using Application.Competitions.Queries.GetMyParticipations;
using Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers;

[ApiController]
[Route("api/competition")]
public class CompetitionController(IMediator mediator) : ControllerBase
{
    // === PUBLIC ENDPOINTS ===

    /// <summary>
    /// Get all competitions with optional status filter
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<CompetitionDto>>> GetCompetitions([FromQuery] string? status = null)
    {
        var query = new GetCompetitionsQuery { Status = status };
        var result = await mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Get competition details by ID or slug
    /// </summary>
    [HttpGet("{idOrSlug}")]
    public async Task<ActionResult<CompetitionDetailDto>> GetCompetitionDetail(string idOrSlug)
    {
        var query = Guid.TryParse(idOrSlug, out var id)
            ? new GetCompetitionDetailQuery { Id = id }
            : new GetCompetitionDetailQuery { Slug = idOrSlug };

        var result = await mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Get novels participating in a competition (sorted by top or newest)
    /// </summary>
    [HttpGet("{competitionId:guid}/novels")]
    public async Task<ActionResult<PagedResult<CompetitionParticipantDto>>> GetCompetitionNovels(
        Guid competitionId,
        [FromQuery] string sortBy = "top",
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = new GetCompetitionNovelsQuery
        {
            CompetitionId = competitionId,
            SortBy = sortBy,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
        var result = await mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Get competition leaderboard (top participants)
    /// </summary>
    [HttpGet("{competitionId:guid}/leaderboard")]
    public async Task<ActionResult<List<CompetitionLeaderboardEntryDto>>> GetLeaderboard(
        Guid competitionId,
        [FromQuery] int top = 10)
    {
        var query = new GetCompetitionLeaderboardQuery
        {
            CompetitionId = competitionId,
            Top = top
        };
        var result = await mediator.Send(query);
        return Ok(result);
    }

    // === AUTHOR ENDPOINTS ===

    /// <summary>
    /// Join a competition with a novel
    /// </summary>
    [HttpPost("{competitionId:guid}/join")]
    [Authorize]
    public async Task<ActionResult<CompetitionParticipantDto>> JoinCompetition(
        Guid competitionId,
        [FromBody] JoinCompetitionRequest request)
    {
        var command = new JoinCompetitionCommand
        {
            CompetitionId = competitionId,
            NovelId = request.NovelId
        };
        var result = await mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Leave a competition
    /// </summary>
    [HttpDelete("{competitionId:guid}/leave/{novelId:guid}")]
    [Authorize]
    public async Task<ActionResult<bool>> LeaveCompetition(Guid competitionId, Guid novelId)
    {
        var command = new LeaveCompetitionCommand
        {
            CompetitionId = competitionId,
            NovelId = novelId
        };
        var result = await mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Get my competition participations
    /// </summary>
    [HttpGet("my-participations")]
    [Authorize]
    public async Task<ActionResult<List<MyCompetitionParticipationDto>>> GetMyParticipations()
    {
        var query = new GetMyParticipationsQuery();
        var result = await mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Get my novels that are eligible to join a specific competition
    /// </summary>
    [HttpGet("{competitionId:guid}/my-eligible-novels")]
    [Authorize]
    public async Task<ActionResult<List<EligibleNovelDto>>> GetMyEligibleNovels(Guid competitionId)
    {
        var query = new GetMyEligibleNovelsQuery { CompetitionId = competitionId };
        var result = await mediator.Send(query);
        return Ok(result);
    }

    // === ADMIN ENDPOINTS ===

    /// <summary>
    /// Create a new competition (Admin only)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<CompetitionDetailDto>> CreateCompetition([FromBody] CreateCompetitionCommand command)
    {
        var result = await mediator.Send(command);
        return CreatedAtAction(nameof(GetCompetitionDetail), new { idOrSlug = result.Id }, result);
    }

    /// <summary>
    /// Update a competition (Admin only)
    /// </summary>
    [HttpPut("{competitionId:guid}")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<CompetitionDetailDto>> UpdateCompetition(
        Guid competitionId,
        [FromBody] UpdateCompetitionCommand command)
    {
        command.Id = competitionId;
        var result = await mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Add extra points to a participant (Admin only)
    /// </summary>
    [HttpPost("{competitionId:guid}/extra-points")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<bool>> AddExtraPoints(
        Guid competitionId,
        [FromBody] AddExtraPointsRequest request)
    {
        var command = new AddExtraPointsCommand
        {
            CompetitionId = competitionId,
            NovelId = request.NovelId,
            ExtraPoints = request.ExtraPoints
        };
        var result = await mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Recalculate rankings for a competition (Admin only)
    /// </summary>
    [HttpPost("{competitionId:guid}/recalculate-rankings")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<List<CompetitionLeaderboardEntryDto>>> RecalculateRankings(Guid competitionId)
    {
        var command = new RecalculateRankingsCommand { CompetitionId = competitionId };
        var result = await mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Finalize competition and save winners (Admin only)
    /// </summary>
    [HttpPost("{competitionId:guid}/finalize")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<List<CompetitionWinnerDto>>> FinalizeCompetition(Guid competitionId)
    {
        var command = new FinalizeCompetitionCommand { CompetitionId = competitionId };
        var result = await mediator.Send(command);
        return Ok(result);
    }
}

// Request DTOs
public class JoinCompetitionRequest
{
    public Guid NovelId { get; set; }
}

public class AddExtraPointsRequest
{
    public Guid NovelId { get; set; }
    public decimal ExtraPoints { get; set; }
}
