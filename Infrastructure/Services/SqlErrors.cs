using Microsoft.Data.SqlClient;

namespace Infrastructure.Services;

/// <summary>What a SQL Server error means, wherever EF Core wrapped it.</summary>
public static class SqlErrors
{
    /// <summary>SQL Server chose this transaction as a deadlock victim and rolled all of it back.</summary>
    public const int DeadlockVictim = 1205;

    public static bool IsDeadlock(Exception? exception)
    {
        for (var e = exception; e != null; e = e.InnerException)
        {
            if (e is SqlException sql && (sql.Number == DeadlockVictim || sql.Errors.Cast<SqlError>().Any(x => x.Number == DeadlockVictim)))
            {
                return true;
            }
        }
        return false;
    }
}
