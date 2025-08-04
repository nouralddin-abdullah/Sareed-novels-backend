using Amazon.Runtime.Internal;
using MediatR;

namespace Application.Rankings.Commands.CalculateAllRankings;

public class CalculateAllRankingsCommand : IRequest<CalculateAllRankingsResult>
{
}

public class CalculateAllRankingsResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = default!;
    public double ExecutionTimeMs { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Error { get; set; }
}
