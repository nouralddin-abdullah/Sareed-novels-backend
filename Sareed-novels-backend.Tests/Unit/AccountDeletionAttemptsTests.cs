using Application.Users.Commands.DeleteAccount;
using Sareed_novels_backend.Tests.Integration;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>The per-account limit on DELETE /api/User/me: five attempts in any hour.</summary>
public class AccountDeletionAttemptsTests
{
    private readonly MutableClock clock = new(new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Five_attempts_pass_then_the_account_waits_until_the_first_is_an_hour_old()
    {
        var attempts = new AccountDeletionAttempts(clock);
        for (var i = 0; i < AccountDeletionAttempts.Limit; i++)
        {
            Assert.True(attempts.TryAcquire("reader"));
            clock.Advance(TimeSpan.FromMinutes(10));
        }

        // 50 minutes after the first attempt.
        Assert.False(attempts.TryAcquire("reader"));
        Assert.False(attempts.TryAcquire("READER")); // user ids compare like everywhere else, ignoring case

        clock.Advance(TimeSpan.FromMinutes(10)); // the first is an hour old now
        Assert.True(attempts.TryAcquire("reader"));
        Assert.False(attempts.TryAcquire("reader"));
    }

    [Fact]
    public void Each_account_has_its_own_limit_and_refused_attempts_dont_count()
    {
        var attempts = new AccountDeletionAttempts(clock);
        for (var i = 0; i < AccountDeletionAttempts.Limit; i++)
        {
            Assert.True(attempts.TryAcquire("reader"));
        }
        for (var i = 0; i < 20; i++)
        {
            Assert.False(attempts.TryAcquire("reader"));
        }

        Assert.True(attempts.TryAcquire("someone-else"));

        clock.Advance(AccountDeletionAttempts.Window);
        Assert.True(attempts.TryAcquire("reader"));
    }
}
