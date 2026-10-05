using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// #77: a chapter's word count and a draft's scheduled publish time, both null for existing chapters. The words are
    /// counted from the stored paragraphs' HTML, which SQL can't read as a reader does, so existing chapters are counted
    /// by the app after the deploy (ChapterWordsBackfillService), not here. The filtered index holds scheduled drafts only.
    /// </summary>
    public partial class AddChapterWordsCountAndPublishAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PublishAt",
                table: "Chapters",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WordsCount",
                table: "Chapters",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_PublishAt",
                table: "Chapters",
                column: "PublishAt",
                filter: "[PublishAt] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "NovelId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Chapters_PublishAt",
                table: "Chapters");

            migrationBuilder.DropColumn(
                name: "PublishAt",
                table: "Chapters");

            migrationBuilder.DropColumn(
                name: "WordsCount",
                table: "Chapters");
        }
    }
}
