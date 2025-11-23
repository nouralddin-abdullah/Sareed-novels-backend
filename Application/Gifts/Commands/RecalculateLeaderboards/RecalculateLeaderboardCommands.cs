using MediatR;

namespace Application.Gifts.Commands.RecalculateLeaderboards;

public class RecalculateWeeklyLeaderboardCommand : IRequest<bool>
{
}

public class RecalculateAllTimeLeaderboardCommand : IRequest<bool>
{
}
