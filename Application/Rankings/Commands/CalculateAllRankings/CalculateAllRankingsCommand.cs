using Amazon.Runtime.Internal;
using MediatR;
using System.Text.Json.Serialization;

namespace Application.Rankings.Commands.CalculateAllRankings;

public class CalculateAllRankingsCommand : IRequest<CalculateAllRankingsResult>
{
}

public class CalculateAllRankingsResult
{
    public bool Success { get; set; }
    /// <summary>Set on failure (OperationFailed), like OperationResult's.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; set; }
    public string Message { get; set; } = default!;
    public double ExecutionTimeMs { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Error { get; set; }
}
