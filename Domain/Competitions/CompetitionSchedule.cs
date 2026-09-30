using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Domain.Entities;

namespace Domain.Competitions;

/// <summary>
/// A competition's status follows its schedule (#42). Every reader, and joining and leaving, use the status in effect
/// at a given time (UTC), <see cref="StatusAt"/>, never the stored <see cref="Competition.Status"/> alone.
/// <para>
/// From the dates: <see cref="CompetitionStatus.Upcoming"/> before <see cref="Competition.ParticipationStartDate"/>,
/// <see cref="CompetitionStatus.Participation"/> from that instant until <see cref="Competition.ParticipationEndDate"/>,
/// and <see cref="CompetitionStatus.Judging"/> from that instant on. A date is the first instant of the phase it opens:
/// at exactly the start date the competition is open, at exactly the end date it is closed. The judgment dates and
/// <see cref="Competition.ResultsDate"/> are for showing; the competition stays in Judging through them and after, until
/// it is finalized.
/// </para>
/// <para>
/// <see cref="CompetitionStatus.Completed"/> never comes from the dates, only from the stored status (finalizing, or an
/// admin). The stored status is an override that counts only where it is further along, in the order Upcoming &lt;
/// Participation &lt; Judging &lt; Completed: an admin can open early (Participation), close early (Judging) or complete
/// early, but can't hold a competition back; to postpone one, move its dates. Stored Upcoming, what a new competition
/// has, overrides nothing.
/// </para>
/// </summary>
public static class CompetitionSchedule
{
    /// <summary>The four statuses, in the order a competition goes through them.</summary>
    public static IReadOnlyList<string> Statuses { get; } =
    [
        CompetitionStatus.Upcoming, CompetitionStatus.Participation, CompetitionStatus.Judging, CompetitionStatus.Completed
    ];

    /// <summary>
    /// The rule, written once, as an expression: queries use it translated to SQL (a CASE, with the time as a
    /// parameter), the in-memory checks compiled (<see cref="Evaluate"/>), so the two can't drift apart. Its value is
    /// the further along of the stored status and the one the dates give. The stored status is always one of the four
    /// names as written in <see cref="CompetitionStatus"/> (<see cref="TryParseStatus"/>), so SQL Server's
    /// case-insensitive comparison and C#'s ordinal one agree on it.
    /// </summary>
    private static readonly Expression<Func<Competition, DateTime, string>> Rule = (c, utcNow) =>
        c.Status == CompetitionStatus.Completed
            ? CompetitionStatus.Completed
            : c.Status == CompetitionStatus.Judging || utcNow >= c.ParticipationEndDate
                ? CompetitionStatus.Judging
                : c.Status == CompetitionStatus.Participation || utcNow >= c.ParticipationStartDate
                    ? CompetitionStatus.Participation
                    : CompetitionStatus.Upcoming;

    private static readonly Func<Competition, DateTime, string> Evaluate = Rule.Compile();

    /// <summary>The status of <paramref name="competition"/> at <paramref name="utcNow"/>.</summary>
    public static string StatusAt(Competition competition, DateTime utcNow) => Evaluate(competition, utcNow);

    /// <summary>For a query: competitions whose status at <paramref name="utcNow"/> is <paramref name="status"/>.</summary>
    public static Expression<Func<Competition, bool>> StatusIs(string status, DateTime utcNow)
    {
        Expression<Func<string>> value = () => status;
        return Where(utcNow, rule => Expression.Equal(rule, value.Body));
    }

    /// <summary>For a query: competitions whose status at <paramref name="utcNow"/> isn't <paramref name="status"/>.</summary>
    public static Expression<Func<Competition, bool>> StatusIsNot(string status, DateTime utcNow)
    {
        Expression<Func<string>> value = () => status;
        return Where(utcNow, rule => Expression.NotEqual(rule, value.Body));
    }

    /// <summary>
    /// One of the four statuses, ignoring case and surrounding spaces, as written in <see cref="CompetitionStatus"/>:
    /// what an admin may store and what the list may be filtered by.
    /// </summary>
    public static bool TryParseStatus(string? value, [NotNullWhen(true)] out string? status)
    {
        var trimmed = value?.Trim();
        status = Statuses.FirstOrDefault(s => string.Equals(s, trimmed, StringComparison.OrdinalIgnoreCase));
        return status is not null;
    }

    /// <summary>
    /// A predicate on the rule's value at <paramref name="utcNow"/>. The time goes in as a captured value, like a
    /// variable in a query, so EF Core sends it as a parameter.
    /// </summary>
    private static Expression<Func<Competition, bool>> Where(DateTime utcNow, Func<Expression, Expression> test)
    {
        Expression<Func<DateTime>> now = () => utcNow;
        var status = new Substitute(Rule.Parameters[1], now.Body).Visit(Rule.Body);
        return Expression.Lambda<Func<Competition, bool>>(test(status), Rule.Parameters[0]);
    }

    private sealed class Substitute(ParameterExpression parameter, Expression value) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == parameter ? value : node;
    }
}
