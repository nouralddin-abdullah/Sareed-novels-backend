using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// #66: every stored counter that counts rows is set, once, from the rows it counts. Until the counters moved with
    /// atomic SQL (SocialCounters, 2026-09-25), the code read them, changed them and wrote whole rows back, some of it in
    /// the background with failures only logged, so concurrent changes and failed writes were lost; deleting a post's
    /// comment never lowered the post's count, and TotalCommentsCount was added at 0 next to the chapters' existing
    /// comments. Nothing ever recounted them: in production post 17f1d72d, with one comment and a reply to it, said 0
    /// comments. From here the atomic code keeps them right.
    /// <para>
    /// Data, set based and idempotent (<see cref="Recount"/>): each counter in <see cref="Counters"/> gets what the live
    /// code maintains for it over a normal history (each one's rule is in the list), on every row of its table: deleted
    /// posts, novels and accounts too, whose counters the live code keeps as well. Each UPDATE touches only the rows whose
    /// stored value differs, so running it again changes nothing. The counted rows are read with HOLDLOCK and the batch
    /// runs at high deadlock priority, so a request that writes a row and its count in one transaction during the deploy
    /// (a comment, a like, a review, a library entry) can't make it lose a change: the request waits for the commit (its
    /// +1 then lands on the recounted value), or, in a deadlock, is the one that fails, never the migration and with it
    /// the startup. Views, points and money aren't counts of rows and stay as they are. Down changes nothing: the
    /// drifted values can't be told apart from right ones, so there is nothing to put back.
    /// </para>
    /// <para>
    /// <see cref="Diagnostic"/> is the same recount as SELECT statements, the file
    /// <c>20261004164302_RecountStoredCounters.Diagnostic.sql</c> next to this one (a test keeps the two equal): run on
    /// production before the deploy, it lists the rows this changes and how far off each counter is.
    /// </para>
    /// </summary>
    public partial class RecountStoredCounters : Migration
    {
        /// <summary>
        /// A stored counter, <see cref="Table"/>.<see cref="Column"/>, what it counts (<see cref="Counts"/>, for the
        /// scripts), and <see cref="Recount"/>: given the table hint the counted rows are read with ("" for none), a
        /// query of one row whose <c>Value</c> is the counter's value for the row <c>t</c> of <see cref="Table"/>.
        /// </summary>
        internal sealed record Counter(string Table, string Column, string Counts, Func<string, string> Recount)
        {
            public string Name => Table + "." + Column;
        }

        /// <summary>
        /// A comment counts while its author hasn't deleted it (IsDeleted = 0; one a moderator removed, or on a chapter or
        /// paragraph since deleted, is gone with its replies). A top-level one counts in one place, where
        /// SocialCounters.AdjustForComment counts it: its post, else its paragraph (and that paragraph's chapter's total),
        /// else its chapter. Replies count only for their author.
        /// </summary>
        private const string VisibleTopLevel = "c.ParentCommentId IS NULL AND c.IsDeleted = 0";

        /// <summary>How the migration reads the counted rows (see the summary above).</summary>
        internal const string HoldLock = " WITH (HOLDLOCK)";

        internal static readonly IReadOnlyList<Counter> Counters =
        [
            // Comments.
            new("Posts", "CommentsCount", "top-level comments on the post, not deleted (replies don't count)",
                h => $"""
                    SELECT COUNT(*) AS Value FROM Comments c{h}
                    WHERE c.PostId = t.Id AND {VisibleTopLevel}
                    """),
            new("ChapterParagraphs", "CommentsCount", "top-level comments on the paragraph, not deleted",
                h => $"""
                    SELECT COUNT(*) AS Value FROM Comments c{h}
                    WHERE c.ParagraphId = t.Id AND c.PostId IS NULL AND {VisibleTopLevel}
                    """),
            new("Chapters", "CommentsCount", "top-level comments on the chapter itself, not deleted (its paragraphs' don't count)",
                h => $"""
                    SELECT COUNT(*) AS Value FROM Comments c{h}
                    WHERE c.ChapterId = t.Id AND c.ParagraphId IS NULL AND c.PostId IS NULL AND {VisibleTopLevel}
                    """),
            new("Chapters", "TotalCommentsCount", "top-level comments on the chapter itself and on its paragraphs, not deleted",
                h => $"""
                    SELECT (SELECT COUNT(*) FROM Comments c{h}
                            WHERE c.ChapterId = t.Id AND c.ParagraphId IS NULL AND c.PostId IS NULL AND {VisibleTopLevel})
                         + (SELECT COUNT(*) FROM ChapterParagraphs p{h} JOIN Comments c{h} ON c.ParagraphId = p.Id
                            WHERE p.ChapterId = t.Id AND c.PostId IS NULL AND {VisibleTopLevel}) AS Value
                    """),
            new("AspNetUsers", "CommentsCount", "the member's comments anywhere, replies included, not deleted (not shown since #54)",
                h => $"""
                    SELECT COUNT(*) AS Value FROM Comments c{h}
                    WHERE c.UserId = t.Id AND c.IsDeleted = 0
                    """),

            // Likes: one row per member and item; a deleted post or comment keeps its likes.
            new("Posts", "LikesCount", "likes of the post",
                h => $"SELECT COUNT(*) AS Value FROM PostLikes l{h} WHERE l.PostId = t.Id"),
            new("Comments", "LikesCount", "likes of the comment",
                h => $"SELECT COUNT(*) AS Value FROM CommentLikes l{h} WHERE l.CommentId = t.Id"),
            new("Reviews", "LikeCount", "likes of the review",
                h => $"SELECT COUNT(*) AS Value FROM ReviewLikes l{h} WHERE l.ReviewId = t.Id"),

            // Reviews (no soft delete: a review deleted or removed is gone). A novel's stats are ReviewsRepository.
            // RefreshNovelReviewStats's SQL, rounded as storing it rounds (decimal(3, 2)).
            new("AspNetUsers", "ReviewsCount", "the member's reviews (not shown since #54)",
                h => $"SELECT COUNT(*) AS Value FROM Reviews v{h} WHERE v.ReviewerId = t.Id"),
            new("Novels", "ReviewCount", "reviews of the novel",
                h => $"SELECT COUNT(*) AS Value FROM Reviews v{h} WHERE v.NovelId = t.Id"),
            Average("AverageWritingQualityScore", "WritingQualityScore", "writing quality"),
            Average("AverageUpdatingStabilityScore", "UpdatingStabilityScore", "updating stability"),
            Average("AverageCharacterDevelopmentScore", "CharacterDevelopmentScore", "character development"),
            Average("AverageWorldBuildingScore", "WorldBuildingScore", "world building"),
            new("Novels", "TotalAverageScore", "average of the four average scores before they are rounded, 0 without reviews",
                h => $"""
                    SELECT CAST((COALESCE(AVG(v.WritingQualityScore), 0.0) + COALESCE(AVG(v.UpdatingStabilityScore), 0.0)
                                 + COALESCE(AVG(v.CharacterDevelopmentScore), 0.0) + COALESCE(AVG(v.WorldBuildingScore), 0.0))
                                / 4.0 AS decimal(3, 2)) AS Value
                    FROM Reviews v{h} WHERE v.NovelId = t.Id
                    """),

            // Library and reading lists.
            new("AspNetUsers", "LibraryNovelsCount", "novels in the member's library (reading progress), deleted and draft ones included",
                h => $"SELECT COUNT(*) AS Value FROM UserNovelProgress u{h} WHERE u.UserId = t.Id"),
            new("ReadingLists", "NovelsCount", "novels on the list, hidden ones included (not shown: lists count what readers can open)",
                h => $"SELECT COUNT(*) AS Value FROM ReadingListNovels n{h} WHERE n.ReadingListId = t.Id"),
            new("ReadingLists", "FollowersCount", "followers of the list",
                h => $"SELECT COUNT(*) AS Value FROM ReadingListFollowers f{h} WHERE f.ReadingListId = t.Id"),

            // Chapters and novels.
            new("Chapters", "ParagraphsCount", "paragraphs of the chapter",
                h => $"SELECT COUNT(*) AS Value FROM ChapterParagraphs p{h} WHERE p.ChapterId = t.Id"),
            new("Novels", "ChapterCount", "published chapters of the novel",
                h => $"SELECT COUNT(*) AS Value FROM Chapters ch{h} WHERE ch.NovelId = t.Id AND ch.Status = N'Published'")
        ];

        private static Counter Average(string column, string score, string words) =>
            new("Novels", column, $"average {words} score of the novel's reviews, 0 without reviews",
                h => $"SELECT CAST(COALESCE(AVG(v.{score}), 0.0) AS decimal(3, 2)) AS Value FROM Reviews v{h} WHERE v.NovelId = t.Id");

        /// <summary>What Up runs: one UPDATE per counter, in the order of <see cref="Counters"/>.</summary>
        internal static readonly string Recount =
            "SET DEADLOCK_PRIORITY HIGH;\n\n"
            + string.Concat(Counters.Select(c => $"""
                -- {c.Name}: {c.Counts}.
                UPDATE t SET {c.Column} = r.Value
                FROM {c.Table} t
                CROSS APPLY ({Continued(c.Recount(HoldLock), 13)}) r
                WHERE t.{c.Column} <> r.Value;


                """))
            + "SET DEADLOCK_PRIORITY NORMAL;\n";

        /// <summary>
        /// The read-only diagnostic: <see cref="Recount"/>'s counters, rows and comparisons as three SELECT statements,
        /// each standing alone. The file next to this migration is this text.
        /// </summary>
        internal static readonly string Diagnostic =
            """
            -- Read-only diagnostic for the migration RecountStoredCounters (#66): what it will change, before it is deployed.
            -- Only SELECT statements, each standing alone; nothing is written. Generated from the migration's own counter
            -- definitions (Infrastructure/Migrations/20261004164302_RecountStoredCounters.cs, RecountStoredCounters.Counters)
            -- and kept equal to them by a test: change the migration, not this file.
            --
            -- Each counter is compared with the recount the migration stores, row by row; a row "changes" exactly when the
            -- migration's UPDATE touches it. The five Novels score columns are in points (two decimals); the rest are counts.
            -- After the migration, statements 1 and 2 report nothing to change.


            -- 1. Summary, one row per counter, in the order the migration recounts them.
            --    RowsToChange: rows the migration updates (StoredTooHigh + StoredTooLow).
            --    TotalAbsoluteDrift: the sum, over all rows, of |stored - recounted|.

            """
            + string.Join("UNION ALL\n", Counters.Select((c, i) => $"""
                SELECT {i + 1} AS Step, N'{c.Name}' AS Counter, COUNT(*) AS RowsChecked,
                       COUNT(CASE WHEN t.{c.Column} <> r.Value THEN 1 END) AS RowsToChange,
                       COUNT(CASE WHEN t.{c.Column} > r.Value THEN 1 END) AS StoredTooHigh,
                       COUNT(CASE WHEN t.{c.Column} < r.Value THEN 1 END) AS StoredTooLow,
                       CAST(ISNULL(SUM(ABS(t.{c.Column} - r.Value)), 0) AS decimal(19, 2)) AS TotalAbsoluteDrift
                FROM {c.Table} t
                CROSS APPLY ({Continued(c.Recount(""), 13)}) r

                """))
            + """
            ORDER BY Step;


            -- 2. Every row the migration changes: the counter, the row's Id, its stored value and the recounted one it gets.

            """
            + string.Join("UNION ALL\n", Counters.Select((c, i) => $"""
                SELECT {i + 1} AS Step, N'{c.Name}' AS Counter, CONVERT(nvarchar(450), t.Id) AS RowId,
                       CAST(t.{c.Column} AS decimal(19, 2)) AS Stored, CAST(r.Value AS decimal(19, 2)) AS Recounted
                FROM {c.Table} t
                CROSS APPLY ({Continued(c.Recount(""), 13)}) r
                WHERE t.{c.Column} <> r.Value

                """))
            + """
            ORDER BY Step, RowId;


            -- 3. What the comment counters assume: a comment is in one place, a post, a paragraph or a chapter, as the API
            --    stores them. Expect no rows for "several places" or "no place". A comment in several places is counted
            --    where SocialCounters counts it (its post, else its paragraph, else its chapter); one in no place only
            --    for its author.
            SELECT Place, COUNT(*) AS Comments, COUNT(CASE WHEN ParentCommentId IS NULL THEN 1 END) AS TopLevel,
                   COUNT(CASE WHEN ParentCommentId IS NULL AND IsDeleted = 0 THEN 1 END) AS TopLevelNotDeleted
            FROM (SELECT c.ParentCommentId, c.IsDeleted,
                         CASE (CASE WHEN c.PostId IS NULL THEN 0 ELSE 1 END + CASE WHEN c.ParagraphId IS NULL THEN 0 ELSE 1 END
                               + CASE WHEN c.ChapterId IS NULL THEN 0 ELSE 1 END)
                             WHEN 0 THEN N'no place'
                             WHEN 1 THEN CASE WHEN c.PostId IS NOT NULL THEN N'post'
                                              WHEN c.ParagraphId IS NOT NULL THEN N'paragraph'
                                              ELSE N'chapter' END
                             ELSE N'several places' END AS Place
                  FROM Comments c) x
            GROUP BY Place
            ORDER BY Place;

            """;

        /// <summary>Indents every line of <paramref name="sql"/> after the first by <paramref name="spaces"/>.</summary>
        private static string Continued(string sql, int spaces) => sql.Replace("\n", "\n" + new string(' ', spaces));

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Recount);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the drifted values can't be told apart from right ones, so there is nothing to put back.
        }
    }
}
