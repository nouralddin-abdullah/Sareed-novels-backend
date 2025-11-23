using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Gifts.Commands.RecalculateLeaderboards;

public class RecalculateWeeklyLeaderboardCommandHandler(
    ILogger<RecalculateWeeklyLeaderboardCommandHandler> logger,
    IGlobalSupporterLeaderboardRepository leaderboardRepository) : IRequestHandler<RecalculateWeeklyLeaderboardCommand, bool>
{
    public async Task<bool> Handle(RecalculateWeeklyLeaderboardCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Recalculating weekly leaderboard");

        await leaderboardRepository.RecalculateWeeklyLeaderboard();

        logger.LogInformation("Weekly leaderboard recalculated successfully");

        return true;
    }
}

public class RecalculateAllTimeLeaderboardCommandHandler(
    ILogger<RecalculateAllTimeLeaderboardCommandHandler> logger,
    IGlobalSupporterLeaderboardRepository leaderboardRepository) : IRequestHandler<RecalculateAllTimeLeaderboardCommand, bool>
{
    public async Task<bool> Handle(RecalculateAllTimeLeaderboardCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Recalculating all-time leaderboard");

        await leaderboardRepository.RecalculateAllTimeLeaderboard();

        logger.LogInformation("All-time leaderboard recalculated successfully");

        return true;
    }
}
