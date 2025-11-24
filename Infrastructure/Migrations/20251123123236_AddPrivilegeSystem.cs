using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivilegeSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NovelPrivileges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NovelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    MaxLockedChapters = table.Column<int>(type: "int", nullable: false, defaultValue: 20),
                    SubscriptionCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SubscriptionDurationDays = table.Column<int>(type: "int", nullable: false, defaultValue: 30),
                    CurrentLockedCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    PrivilegeStartSequence = table.Column<int>(type: "int", nullable: true),
                    LastDailyUnlockDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalDailyUnlocksPerformed = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    MinPublishedRequired = table.Column<int>(type: "int", nullable: false, defaultValue: 10),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NovelPrivileges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NovelPrivileges_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NovelPrivilegeSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NovelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SubscribedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    AmountPaid = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NovelPrivilegeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NovelPrivilegeSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NovelPrivilegeSubscriptions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_NovelPrivilegeSubscriptions_NovelPrivileges_NovelPrivilegeId",
                        column: x => x.NovelPrivilegeId,
                        principalTable: "NovelPrivileges",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_NovelPrivilegeSubscriptions_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NovelPrivileges_Enabled_LockedCount",
                table: "NovelPrivileges",
                columns: new[] { "IsEnabled", "CurrentLockedCount" });

            migrationBuilder.CreateIndex(
                name: "IX_NovelPrivileges_IsEnabled",
                table: "NovelPrivileges",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_NovelPrivileges_NovelId_Unique",
                table: "NovelPrivileges",
                column: "NovelId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NovelPrivilegeSubscriptions_Active_Expires",
                table: "NovelPrivilegeSubscriptions",
                columns: new[] { "IsActive", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NovelPrivilegeSubscriptions_Novel_User",
                table: "NovelPrivilegeSubscriptions",
                columns: new[] { "NovelId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_NovelPrivilegeSubscriptions_NovelPrivilegeId",
                table: "NovelPrivilegeSubscriptions",
                column: "NovelPrivilegeId");

            migrationBuilder.CreateIndex(
                name: "IX_NovelPrivilegeSubscriptions_SubscribedAt",
                table: "NovelPrivilegeSubscriptions",
                column: "SubscribedAt");

            migrationBuilder.CreateIndex(
                name: "IX_NovelPrivilegeSubscriptions_User_Active_Expires",
                table: "NovelPrivilegeSubscriptions",
                columns: new[] { "UserId", "IsActive", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NovelPrivilegeSubscriptions");

            migrationBuilder.DropTable(
                name: "NovelPrivileges");
        }
    }
}
