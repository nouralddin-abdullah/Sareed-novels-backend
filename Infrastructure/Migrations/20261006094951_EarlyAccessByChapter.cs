using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Early access by chapter (#94): a chapter's own lock (when it started, when it was freed) instead of a position
    /// among the published chapters, and a novel's days or subscribers only. The columns of the positional window stay
    /// for one release (NovelPrivilege), so that the version before can still run against the database.
    /// </summary>
    public partial class EarlyAccessByChapter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EarlyAccessDays",
                table: "NovelPrivileges",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SubscribersOnly",
                table: "NovelPrivileges",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "EarlyAccessFreedAt",
                table: "Chapters",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EarlyAccessFrom",
                table: "Chapters",
                type: "datetime2",
                nullable: true);

            // The novels that had early access before keep it, with the default days.
            migrationBuilder.Sql("""
                UPDATE NovelPrivileges SET EarlyAccessDays = 7 WHERE EarlyAccessDays IS NULL AND SubscribersOnly = 0;
                """);

            // A chapter locked now stays locked, from now (the deploy is when its lock starts, as turning early access on
            // would), and a free one stays free: nothing readers see changes at the deploy. Locked now is what the
            // reader's chapter read went by: early access on, a positive stored count, and a published position at or past
            // the start; never among the first 10 published chapters. (In production, 2026-10-06, no chapter is.)
            migrationBuilder.Sql("""
                UPDATE c SET c.EarlyAccessFrom = SYSUTCDATETIME()
                FROM Chapters c
                JOIN NovelPrivileges p ON p.NovelId = c.NovelId
                WHERE p.IsEnabled = 1
                  AND p.CurrentLockedCount > 0
                  AND p.PrivilegeStartSequence IS NOT NULL
                  AND c.Status = N'Published'
                  AND c.PublishedChapterSequence >= p.PrivilegeStartSequence
                  AND c.PublishedChapterSequence > 10;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_NovelPrivileges_EarlyAccessMode",
                table: "NovelPrivileges",
                sql: "([SubscribersOnly] = 1 AND [EarlyAccessDays] IS NULL) OR ([SubscribersOnly] = 0 AND [EarlyAccessDays] IS NOT NULL AND [EarlyAccessDays] BETWEEN 1 AND 30)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_NovelPrivileges_EarlyAccessMode",
                table: "NovelPrivileges");

            migrationBuilder.DropColumn(
                name: "EarlyAccessDays",
                table: "NovelPrivileges");

            migrationBuilder.DropColumn(
                name: "SubscribersOnly",
                table: "NovelPrivileges");

            migrationBuilder.DropColumn(
                name: "EarlyAccessFreedAt",
                table: "Chapters");

            migrationBuilder.DropColumn(
                name: "EarlyAccessFrom",
                table: "Chapters");
        }
    }
}
