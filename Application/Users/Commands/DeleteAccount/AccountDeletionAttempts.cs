namespace Application.Users.Commands.DeleteAccount;

/// <summary>
/// The per-account limit on DELETE /api/User/me: at most <see cref="Limit"/> attempts in any <see cref="Window"/>, so a
/// stolen session can't use the endpoint to guess the account's password. Counted in memory on this instance (the API
/// runs as a single instance); a restart forgets the counts.
/// </summary>
public sealed class AccountDeletionAttempts(TimeProvider time)
{
    public const int Limit = 5;

    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly Dictionary<string, Queue<DateTime>> attempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object sync = new();
    private DateTime nextSweepAt = DateTime.MinValue;

    /// <summary>Counts an attempt by <paramref name="userId"/>, or returns false (counting nothing) when it is over the limit.</summary>
    public bool TryAcquire(string userId)
    {
        var now = time.GetUtcNow().UtcDateTime;
        lock (sync)
        {
            SweepIfDue(now);

            if (!attempts.TryGetValue(userId, out var recent))
            {
                recent = new Queue<DateTime>(Limit);
                attempts[userId] = recent;
            }
            Expire(recent, now);

            if (recent.Count >= Limit)
            {
                return false;
            }
            recent.Enqueue(now);
            return true;
        }
    }

    private static void Expire(Queue<DateTime> recent, DateTime now)
    {
        while (recent.Count > 0 && recent.Peek() <= now - Window)
        {
            recent.Dequeue();
        }
    }

    // Accounts that stopped trying are forgotten, so the table only holds the last window's attempts.
    private void SweepIfDue(DateTime now)
    {
        if (now < nextSweepAt)
        {
            return;
        }
        nextSweepAt = now + Window;

        foreach (var (userId, recent) in attempts.ToList())
        {
            Expire(recent, now);
            if (recent.Count == 0)
            {
                attempts.Remove(userId);
            }
        }
    }
}
