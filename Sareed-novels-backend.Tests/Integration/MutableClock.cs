namespace Sareed_novels_backend.Tests.Integration;

/// <summary>A clock tests move by hand: the push and Play Billing workers' retry schedules.</summary>
public sealed class MutableClock(DateTime utcNow) : TimeProvider
{
    public DateTime UtcNow { get; set; } = utcNow;

    public void Advance(TimeSpan by) => UtcNow += by;

    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(UtcNow, DateTimeKind.Utc));
}
