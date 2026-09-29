using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// #33, reopened: chapters get the time they first came out, <c>Chapters.PublishedAt</c> (datetime2 NULL, UTC; null
    /// while a chapter has never been published), which the library's <c>lastChapterPublishedAt</c> reads.
    /// Data, set based and idempotent (<see cref="Backfill"/>): a chapter's first publish is known from the first
    /// NewChapterInLibrary notification sent for it. Those are written only after a chapter is saved as Published
    /// (created published, or a draft published), and every one of them has named its chapter in RelatedEntityId with
    /// RelatedEntityType 'Chapter' since notifications began (2025-11-15). So a published chapter gets its earliest one,
    /// or its CreatedAt when there is none (nobody had the novel in their library then, or it predates notifications);
    /// an unpublished chapter with one was published before and keeps that time, as unpublishing keeps it from now on;
    /// a draft without one has never been known to be published and stays null. Running it again changes nothing.
    /// Down drops the column.
    /// </summary>
    public partial class AddChapterPublishedAt : Migration
    {
        internal const string Backfill = """
            UPDATE c
            SET PublishedAt = CASE WHEN c.Status = N'Published' THEN COALESCE(n.FirstNotifiedAt, c.CreatedAt)
                                   ELSE n.FirstNotifiedAt END
            FROM Chapters c
            LEFT JOIN (SELECT RelatedEntityId AS ChapterId, MIN(CreatedAt) AS FirstNotifiedAt
                       FROM Notifications
                       WHERE Type = N'NewChapterInLibrary' AND RelatedEntityType = N'Chapter' AND RelatedEntityId IS NOT NULL
                       GROUP BY RelatedEntityId) n ON n.ChapterId = c.Id
            WHERE c.PublishedAt IS NULL
              AND (c.Status = N'Published' OR n.FirstNotifiedAt IS NOT NULL);
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "Chapters",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql(Backfill);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "Chapters");
        }
    }
}
