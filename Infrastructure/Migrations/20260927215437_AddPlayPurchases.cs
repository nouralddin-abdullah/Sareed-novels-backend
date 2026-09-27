using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayPurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayPurchases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseToken = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ProductId = table.Column<string>(type: "varchar(150)", unicode: false, maxLength: 150, nullable: true),
                    OrderId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    Points = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Credited"),
                    IsTestPurchase = table.Column<bool>(type: "bit", nullable: false),
                    PurchasedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextConsumeAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConsumeAttempts = table.Column<int>(type: "int", nullable: false),
                    LastConsumeError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidedReason = table.Column<int>(type: "int", nullable: true),
                    VoidedSource = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayPurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayPurchases_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaySyncCursors",
                columns: table => new
                {
                    Name = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    SyncedUntil = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaySyncCursors", x => x.Name);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayPurchases_NextConsumeAttemptAt",
                table: "PlayPurchases",
                column: "NextConsumeAttemptAt");

            migrationBuilder.CreateIndex(
                name: "IX_PlayPurchases_PurchaseToken_Unique",
                table: "PlayPurchases",
                column: "PurchaseToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayPurchases_UserId",
                table: "PlayPurchases",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayPurchases");

            migrationBuilder.DropTable(
                name: "PlaySyncCursors");
        }
    }
}
