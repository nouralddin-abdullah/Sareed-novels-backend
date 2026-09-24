using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SqlSearchAndUniqueViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchTitle",
                table: "Novels",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SearchName",
                table: "NovelEntities",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "NovelEntities",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SearchName",
                table: "AspNetUsers",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "DailyUniqueViews",
                columns: table => new
                {
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Day = table.Column<DateTime>(type: "date", nullable: false),
                    VisitorKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NovelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyUniqueViews", x => new { x.TargetId, x.Day, x.VisitorKey });
                    table.ForeignKey(
                        name: "FK_DailyUniqueViews_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Novels_SearchTitle",
                table: "Novels",
                column: "SearchTitle");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_SearchName",
                table: "AspNetUsers",
                column: "SearchName");

            migrationBuilder.CreateIndex(
                name: "IX_DailyUniqueViews_NovelId_Day_Kind",
                table: "DailyUniqueViews",
                columns: new[] { "NovelId", "Day", "Kind" })
                .Annotation("SqlServer:Include", new[] { "VisitorKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyUniqueViews");

            migrationBuilder.DropIndex(
                name: "IX_Novels_SearchTitle",
                table: "Novels");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_SearchName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SearchTitle",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "SearchName",
                table: "NovelEntities");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "NovelEntities");

            migrationBuilder.DropColumn(
                name: "SearchName",
                table: "AspNetUsers");
        }
    }
}
