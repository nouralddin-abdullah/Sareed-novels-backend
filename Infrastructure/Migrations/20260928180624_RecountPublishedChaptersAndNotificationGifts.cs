using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// The one migration of #25 (the app's small API fixes).
    /// Data, set based and idempotent: Novels.ChapterCount counts published chapters only (what the chapter list, search
    /// and recommendations count), so every novel is recounted; only the rows that differ change (in production 3 of
    /// 76 novels, which counted their drafts), and running it again changes nothing. Down recounts every chapter, the
    /// rule before.
    /// Schema: Notifications.GiftId and GiftCount, nullable, no foreign key: a GiftReceived notification's gift and how
    /// many, which its message only says in words (gift notifications from before have none).
    /// </summary>
    public partial class RecountPublishedChaptersAndNotificationGifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GiftCount",
                table: "Notifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GiftId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE n
                SET ChapterCount = c.Published
                FROM Novels n
                CROSS APPLY (SELECT COUNT(*) AS Published FROM Chapters ch
                             WHERE ch.NovelId = n.Id AND ch.Status = N'Published') c
                WHERE n.ChapterCount <> c.Published;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE n
                SET ChapterCount = c.AllChapters
                FROM Novels n
                CROSS APPLY (SELECT COUNT(*) AS AllChapters FROM Chapters ch WHERE ch.NovelId = n.Id) c
                WHERE n.ChapterCount <> c.AllChapters;
                """);

            migrationBuilder.DropColumn(
                name: "GiftCount",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "GiftId",
                table: "Notifications");
        }
    }
}
