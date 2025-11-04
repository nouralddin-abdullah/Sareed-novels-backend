using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingListsFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReadingLists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CoverImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NovelsCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    FollowersCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingLists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingLists_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadingListFollowers",
                columns: table => new
                {
                    ReadingListId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FollowedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingListFollowers", x => new { x.ReadingListId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ReadingListFollowers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReadingListFollowers_ReadingLists_ReadingListId",
                        column: x => x.ReadingListId,
                        principalTable: "ReadingLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadingListNovels",
                columns: table => new
                {
                    ReadingListId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NovelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OrderIndex = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingListNovels", x => new { x.ReadingListId, x.NovelId });
                    table.ForeignKey(
                        name: "FK_ReadingListNovels_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReadingListNovels_ReadingLists_ReadingListId",
                        column: x => x.ReadingListId,
                        principalTable: "ReadingLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingListFollowers_ReadingListId",
                table: "ReadingListFollowers",
                column: "ReadingListId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingListFollowers_UserId",
                table: "ReadingListFollowers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingListNovels_List_Order",
                table: "ReadingListNovels",
                columns: new[] { "ReadingListId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingListNovels_NovelId",
                table: "ReadingListNovels",
                column: "NovelId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingLists_IsPublic",
                table: "ReadingLists",
                column: "IsPublic");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingLists_Public_Followers",
                table: "ReadingLists",
                columns: new[] { "IsPublic", "FollowersCount" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingLists_UserId",
                table: "ReadingLists",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingLists_UserId_Name",
                table: "ReadingLists",
                columns: new[] { "UserId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReadingListFollowers");

            migrationBuilder.DropTable(
                name: "ReadingListNovels");

            migrationBuilder.DropTable(
                name: "ReadingLists");
        }
    }
}
