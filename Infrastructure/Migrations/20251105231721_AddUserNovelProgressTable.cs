using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserNovelProgressTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserNovelProgress",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    NovelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastReadChapterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastReadChapterNumber = table.Column<int>(type: "int", nullable: false),
                    LastReadAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNovelProgress", x => new { x.UserId, x.NovelId });
                    table.ForeignKey(
                        name: "FK_UserNovelProgress_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserNovelProgress_Chapters_LastReadChapterId",
                        column: x => x.LastReadChapterId,
                        principalTable: "Chapters",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserNovelProgress_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserNovelProgress_LastReadAt",
                table: "UserNovelProgress",
                column: "LastReadAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserNovelProgress_LastReadChapterId",
                table: "UserNovelProgress",
                column: "LastReadChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNovelProgress_NovelId",
                table: "UserNovelProgress",
                column: "NovelId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNovelProgress_User_LastRead",
                table: "UserNovelProgress",
                columns: new[] { "UserId", "LastReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserNovelProgress_UserId",
                table: "UserNovelProgress",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserNovelProgress");
        }
    }
}
