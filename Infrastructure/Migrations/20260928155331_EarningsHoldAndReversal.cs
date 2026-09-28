using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Payouts (#22): only earnings can be withdrawn, after a hold, and a refund takes back the earnings it paid for.
    /// Schema:
    /// - PointTransactions.AvailableAt: when an earning row (GiftReceived, PrivilegeRevenue) becomes withdrawable.
    /// - PointTransactions.ReversedTransactionId: on EarningReversed rows, the earning row taken back (no foreign key).
    /// - IX_PointTransactions_User_Type_Available (what a user can withdraw), IX_PointTransactions_RelatedRequest and
    ///   IX_PointTransactions_ReversedTransaction (the refund clawback).
    /// - CK_PointTransactions_EarningHasAvailableAt: an earning row must say when it is released.
    /// Data, idempotent: every earning row written before this is released at once (AvailableAt = CreatedAt), so no
    /// author loses what they already earned; the hold applies to earnings from the deploy on. Nothing else changes:
    /// balances stay as they are, and other rows keep AvailableAt null. Down drops all of it.
    /// </summary>
    public partial class EarningsHoldAndReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AvailableAt",
                table: "PointTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversedTransactionId",
                table: "PointTransactions",
                type: "uniqueidentifier",
                nullable: true);

            // Earnings from before the hold existed are released at once (before the check constraint, which needs it).
            migrationBuilder.Sql("""
                UPDATE PointTransactions
                SET AvailableAt = CreatedAt
                WHERE Type IN (N'GiftReceived', N'PrivilegeRevenue') AND AvailableAt IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PointTransactions_RelatedRequest",
                table: "PointTransactions",
                column: "RelatedRequestId",
                filter: "[RelatedRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PointTransactions_ReversedTransaction",
                table: "PointTransactions",
                column: "ReversedTransactionId",
                filter: "[ReversedTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PointTransactions_User_Type_Available",
                table: "PointTransactions",
                columns: new[] { "UserId", "Type", "AvailableAt" })
                .Annotation("SqlServer:Include", new[] { "Amount", "ReversedTransactionId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PointTransactions_EarningHasAvailableAt",
                table: "PointTransactions",
                sql: "[Type] NOT IN (N'GiftReceived', N'PrivilegeRevenue') OR [AvailableAt] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PointTransactions_RelatedRequest",
                table: "PointTransactions");

            migrationBuilder.DropIndex(
                name: "IX_PointTransactions_ReversedTransaction",
                table: "PointTransactions");

            migrationBuilder.DropIndex(
                name: "IX_PointTransactions_User_Type_Available",
                table: "PointTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PointTransactions_EarningHasAvailableAt",
                table: "PointTransactions");

            migrationBuilder.DropColumn(
                name: "AvailableAt",
                table: "PointTransactions");

            migrationBuilder.DropColumn(
                name: "ReversedTransactionId",
                table: "PointTransactions");
        }
    }
}
