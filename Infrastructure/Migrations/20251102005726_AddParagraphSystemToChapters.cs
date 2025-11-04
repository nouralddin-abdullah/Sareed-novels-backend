using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParagraphSystemToChapters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParagraphId",
                table: "Comments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Comments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Content",
                table: "Chapters",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<int>(
                name: "ParagraphsCount",
                table: "Chapters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalCommentsCount",
                table: "Chapters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ChapterParagraphs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChapterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "text"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CommentsCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChapterParagraphs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChapterParagraphs_Chapters_ChapterId",
                        column: x => x.ChapterId,
                        principalTable: "Chapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_Paragraph_Parent",
                table: "Comments",
                columns: new[] { "ParagraphId", "ParentCommentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_ParagraphId",
                table: "Comments",
                column: "ParagraphId");

            migrationBuilder.CreateIndex(
                name: "IX_ChapterParagraphs_Chapter_Order",
                table: "ChapterParagraphs",
                columns: new[] { "ChapterId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_ChapterParagraphs_ChapterId",
                table: "ChapterParagraphs",
                column: "ChapterId");

            migrationBuilder.AddForeignKey(
                name: "FK_Comments_ChapterParagraphs_ParagraphId",
                table: "Comments",
                column: "ParagraphId",
                principalTable: "ChapterParagraphs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Comments_ChapterParagraphs_ParagraphId",
                table: "Comments");

            migrationBuilder.DropTable(
                name: "ChapterParagraphs");

            migrationBuilder.DropIndex(
                name: "IX_Comments_Paragraph_Parent",
                table: "Comments");

            migrationBuilder.DropIndex(
                name: "IX_Comments_ParagraphId",
                table: "Comments");

            migrationBuilder.DropColumn(
                name: "ParagraphId",
                table: "Comments");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Comments");

            migrationBuilder.DropColumn(
                name: "ParagraphsCount",
                table: "Chapters");

            migrationBuilder.DropColumn(
                name: "TotalCommentsCount",
                table: "Chapters");

            migrationBuilder.AlterColumn<string>(
                name: "Content",
                table: "Chapters",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }
    }
}
