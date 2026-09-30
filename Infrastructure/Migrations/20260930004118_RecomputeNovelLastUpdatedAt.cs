using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// #39: a novel's LastUpdatedAt is when one of its chapters last came out, or when the novel was created while none
    /// has. It used to move whenever a chapter was created, drafts included, and not when a draft was published; from
    /// #39 the chapter handlers move it only when a chapter comes out, and this puts the rows already there under the same
    /// rule. Data, set based and idempotent (<see cref="Recompute"/>): every novel gets the latest PublishedAt of its
    /// chapters (one unpublished since counts too: it came out then), or its CreatedAt when no chapter has come out. Only
    /// the rows that differ change, and running it again changes nothing. A chapter deleted after it came out isn't in the
    /// data any more, so it no longer counts. Down puts back the rule before: the latest CreatedAt of its chapters, drafts
    /// included, or its CreatedAt.
    /// </summary>
    public partial class RecomputeNovelLastUpdatedAt : Migration
    {
        internal const string Recompute = """
            UPDATE n
            SET LastUpdatedAt = r.LastUpdatedAt
            FROM Novels n
            CROSS APPLY (SELECT CASE WHEN MAX(c.PublishedAt) > n.CreatedAt THEN MAX(c.PublishedAt) ELSE n.CreatedAt END
                                    AS LastUpdatedAt
                         FROM Chapters c
                         WHERE c.NovelId = n.Id) r
            WHERE n.LastUpdatedAt <> r.LastUpdatedAt;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Recompute);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE n
                SET LastUpdatedAt = r.LastUpdatedAt
                FROM Novels n
                CROSS APPLY (SELECT CASE WHEN MAX(c.CreatedAt) > n.CreatedAt THEN MAX(c.CreatedAt) ELSE n.CreatedAt END
                                        AS LastUpdatedAt
                             FROM Chapters c
                             WHERE c.NovelId = n.Id) r
                WHERE n.LastUpdatedAt <> r.LastUpdatedAt;
                """);
        }
    }
}
