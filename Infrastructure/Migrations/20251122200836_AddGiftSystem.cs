using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGiftSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Gifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Gifts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlobalSupporterLeaderboards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TotalPointsGifted = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    TotalGiftsCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    Period = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalSupporterLeaderboards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GlobalSupporterLeaderboards_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GiftTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NovelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SenderId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    TotalCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiftTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GiftTransactions_AspNetUsers_SenderId",
                        column: x => x.SenderId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GiftTransactions_Gifts_GiftId",
                        column: x => x.GiftId,
                        principalTable: "Gifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GiftTransactions_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Gifts_Cost",
                table: "Gifts",
                column: "Cost");

            migrationBuilder.CreateIndex(
                name: "IX_Gifts_IsActive",
                table: "Gifts",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_GiftTransactions_CreatedAt",
                table: "GiftTransactions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_GiftTransactions_GiftId",
                table: "GiftTransactions",
                column: "GiftId");

            migrationBuilder.CreateIndex(
                name: "IX_GiftTransactions_Novel_Created",
                table: "GiftTransactions",
                columns: new[] { "NovelId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GiftTransactions_Sender_Created",
                table: "GiftTransactions",
                columns: new[] { "SenderId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalSupporterLeaderboard_Period_Rank",
                table: "GlobalSupporterLeaderboards",
                columns: new[] { "Period", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalSupporterLeaderboard_User_Period",
                table: "GlobalSupporterLeaderboards",
                columns: new[] { "UserId", "Period" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GiftTransactions");

            migrationBuilder.DropTable(
                name: "GlobalSupporterLeaderboards");

            migrationBuilder.DropTable(
                name: "Gifts");
        }
    }
}
