using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakePrivilegeSubscriptionsPermanent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NovelPrivilegeSubscriptions_Active_Expires",
                table: "NovelPrivilegeSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_NovelPrivilegeSubscriptions_User_Active_Expires",
                table: "NovelPrivilegeSubscriptions");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "NovelPrivilegeSubscriptions");

            migrationBuilder.DropColumn(
                name: "SubscriptionDurationDays",
                table: "NovelPrivileges");

            migrationBuilder.CreateIndex(
                name: "IX_NovelPrivilegeSubscriptions_Active",
                table: "NovelPrivilegeSubscriptions",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_NovelPrivilegeSubscriptions_User_Active",
                table: "NovelPrivilegeSubscriptions",
                columns: new[] { "UserId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NovelPrivilegeSubscriptions_Active",
                table: "NovelPrivilegeSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_NovelPrivilegeSubscriptions_User_Active",
                table: "NovelPrivilegeSubscriptions");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "NovelPrivilegeSubscriptions",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "SubscriptionDurationDays",
                table: "NovelPrivileges",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.CreateIndex(
                name: "IX_NovelPrivilegeSubscriptions_Active_Expires",
                table: "NovelPrivilegeSubscriptions",
                columns: new[] { "IsActive", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NovelPrivilegeSubscriptions_User_Active_Expires",
                table: "NovelPrivilegeSubscriptions",
                columns: new[] { "UserId", "IsActive", "ExpiresAt" });
        }
    }
}
