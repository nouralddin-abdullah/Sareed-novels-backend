using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// #42: a competition's status follows its schedule, and after its participation ends it is Judging until it is
    /// finalized (Completed). A competition nobody took part in has nothing to judge, but until #42 finalizing one failed
    /// (500), so such a competition stayed in Judging for good: in production «انا مميز», whose participation ended on
    /// 30 Dec 2025 and results date passed on 1 Feb 2026, stored as Upcoming, with no participants. Data, set based and
    /// idempotent (<see cref="Complete"/>): every competition not completed whose participation has ended and whose
    /// results date has passed, and that has no participants, is completed, as finalizing it now does. One with
    /// participants stays in Judging until an admin finalizes it and its winners are chosen; one not ended yet is left
    /// to its dates. Down leaves them completed: under the rule before #42 the stored status was the status, and
    /// Completed is what an ended competition should show there too.
    /// </summary>
    public partial class CompleteEndedCompetitionsWithoutParticipants : Migration
    {
        internal const string Complete = """
            UPDATE c
            SET Status = N'Completed', UpdatedAt = SYSUTCDATETIME()
            FROM Competitions c
            WHERE c.Status <> N'Completed'
              AND c.ParticipationEndDate <= SYSUTCDATETIME()
              AND c.ResultsDate <= SYSUTCDATETIME()
              AND NOT EXISTS (SELECT 1 FROM CompetitionParticipants p WHERE p.CompetitionId = c.Id);
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Complete);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo (see above).
        }
    }
}
