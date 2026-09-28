using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Schema, for #17:
    /// - Gifts.NameAr: the gift's Arabic name, for the GiftReceived notification and wallet entries.
    /// - PointTransactions.NovelId, GiftId, GiftCount: what a gift or privilege entry was about (no foreign keys: the
    ///   ledger outlives novels and gifts). Null for every entry written before this.
    /// Data, set-based and idempotent:
    /// 1. NameAr for the eight gifts of the catalog by their English name (the list production serves, checked with a
    ///    public GET on 2026-09-28); any other gift gets its English name until an admin sets one.
    /// 2. Wallet entries written in English are rewritten in the Arabic of new entries (TransactionDescriptions), but
    ///    only where their pattern is read without guessing: approved recharges and withdrawals (numbers and a method
    ///    name), gifts sent (the gift is matched against the catalog's names, the rest is the title), privilege
    ///    subscriptions and their revenue (a fixed prefix, then the title), and gifts received when " on " appears once
    ///    after the sender (it separates the sender's name from the title; with more than one the split would be a
    ///    guess, so those stay English). A rewritten entry no longer matches its English pattern, so a rerun changes
    ///    nothing. Down drops the columns and leaves the descriptions Arabic.
    /// </summary>
    public partial class ArabicGiftNamesAndTransactionDetails : Migration
    {
        private static readonly (string Name, string NameAr)[] GiftNames =
        [
            ("Rose", "وردة"),
            ("Pizza", "بيتزا"),
            ("Book", "كتاب"),
            ("Crown", "تاج"),
            ("Scepter", "صولجان"),
            ("Castle", "قلعة"),
            ("Dragon", "تنين"),
            ("Galaxy", "مجرة"),
        ];

        private const string Recharge = "Recharge approved: ";
        private const string Withdrawal = "Withdrawal approved: ";
        private const string Subscribed = "Subscribed to privilege for novel: ";
        private const string Revenue = "Privilege subscription revenue from novel: ";

        /// <summary>
        /// "{points} points ({egp} EGP via {method})" after the prefix, in Arabic (the recharge and withdrawal
        /// descriptions' shared tail; the method's Arabic name as PaymentMethod.ArabicName gives it).
        /// </summary>
        private const string AmountsInArabic = """
            REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                SUBSTRING(Description, {0}, 500),
                N' points (', N' نقطة ('), N' EGP via ', N' جنيه عبر '),
                N'VodafoneCash)', N'فودافون كاش)'), N'InstaPay)', N'إنستاباي)'), N'PayPal)', N'باي بال)')
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GiftCount",
                table: "PointTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GiftId",
                table: "PointTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NovelId",
                table: "PointTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameAr",
                table: "Gifts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // 1. Arabic gift names; a name an admin already set is kept.
            foreach (var (name, nameAr) in GiftNames)
            {
                migrationBuilder.Sql($"UPDATE Gifts SET NameAr = N'{nameAr}' WHERE Name = N'{name}' AND NameAr = N'';");
            }
            migrationBuilder.Sql("UPDATE Gifts SET NameAr = Name WHERE NameAr = N'';");

            // 2. English wallet entries whose pattern is certain, in the Arabic new entries use.
            migrationBuilder.Sql($"""
                UPDATE PointTransactions
                SET Description = LEFT(N'شحن رصيد: ' + {string.Format(AmountsInArabic, Recharge.Length + 1)}, 500)
                WHERE Type IN (N'RechargeApproved', N'Recharge')
                  AND Description LIKE N'{Recharge}[0-9]% points (% EGP via %)';
                """);

            migrationBuilder.Sql($"""
                UPDATE PointTransactions
                SET Description = LEFT(N'سحب رصيد: ' + {string.Format(AmountsInArabic, Withdrawal.Length + 1)}, 500)
                WHERE Type IN (N'WithdrawalApproved', N'Withdrawal')
                  AND Description LIKE N'{Withdrawal}[0-9]% points (% EGP via %)';
                """);

            migrationBuilder.Sql($"""
                UPDATE PointTransactions
                SET Description = LEFT(N'اشتراك دائم في امتيازات رواية «' + SUBSTRING(Description, {Subscribed.Length + 1}, 500) + N'»', 500)
                WHERE Type = N'PrivilegeSubscription' AND Description LIKE N'{Subscribed}%';
                """);

            migrationBuilder.Sql($"""
                UPDATE PointTransactions
                SET Description = LEFT(N'عائد اشتراك في امتيازات رواية «' + SUBSTRING(Description, {Revenue.Length + 1}, 500) + N'»', 500)
                WHERE Type = N'PrivilegeRevenue' AND Description LIKE N'{Revenue}%';
                """);

            // "Sent {count}x {gift} to {title}": the count is digits, the gift one of the catalog's names, so the prefix
            // up to " to " is known exactly and the rest is the title. (Lengths guard every SUBSTRING, whose arguments
            // SQL Server may compute before the WHERE clause filters the row out.)
            migrationBuilder.Sql("""
                UPDATE t
                SET Description = LEFT(N'أرسلت ' + g.NameAr + N' ×' + c.Cnt + N' إلى رواية «'
                                       + SUBSTRING(t.Description, DATALENGTH(p.Prefix) / 2 + 1, 500) + N'»', 500)
                FROM PointTransactions t
                CROSS APPLY (SELECT CHARINDEX(N'x ', t.Description) AS X) x
                CROSS APPLY (SELECT SUBSTRING(t.Description, 6, CASE WHEN x.X > 6 THEN x.X - 6 ELSE 0 END) AS Cnt) c
                JOIN Gifts g ON 1 = 1
                CROSS APPLY (SELECT N'Sent ' + c.Cnt + N'x ' + g.Name + N' to ' AS Prefix) p
                WHERE t.Type = N'GiftSent'
                  AND t.Description LIKE N'Sent [0-9]%x % to %'
                  AND c.Cnt <> N'' AND c.Cnt NOT LIKE N'%[^0-9]%'
                  AND LEFT(t.Description, DATALENGTH(p.Prefix) / 2) = p.Prefix;
                """);

            // "Received {count}x {gift} from {sender} on {title}": as above up to " from ", then the sender's name and the
            // title, separated by " on " - rewritten only when " on " appears exactly once there.
            migrationBuilder.Sql("""
                UPDATE t
                SET Description = LEFT(N'استلمت ' + g.NameAr + N' ×' + c.Cnt + N' من '
                                       + LEFT(r.Rest, CASE WHEN o.OnAt > 1 THEN o.OnAt - 1 ELSE 0 END)
                                       + N' على رواية «' + SUBSTRING(r.Rest, o.OnAt + 4, 500) + N'»', 500)
                FROM PointTransactions t
                CROSS APPLY (SELECT CHARINDEX(N'x ', t.Description) AS X) x
                CROSS APPLY (SELECT SUBSTRING(t.Description, 10, CASE WHEN x.X > 10 THEN x.X - 10 ELSE 0 END) AS Cnt) c
                JOIN Gifts g ON 1 = 1
                CROSS APPLY (SELECT N'Received ' + c.Cnt + N'x ' + g.Name + N' from ' AS Prefix) p
                CROSS APPLY (SELECT SUBSTRING(t.Description, DATALENGTH(p.Prefix) / 2 + 1, 500) AS Rest) r
                CROSS APPLY (SELECT CHARINDEX(N' on ', r.Rest) AS OnAt) o
                WHERE t.Type = N'GiftReceived'
                  AND t.Description LIKE N'Received [0-9]%x % from % on %'
                  AND c.Cnt <> N'' AND c.Cnt NOT LIKE N'%[^0-9]%'
                  AND LEFT(t.Description, DATALENGTH(p.Prefix) / 2) = p.Prefix
                  AND o.OnAt > 1
                  AND CHARINDEX(N' on ', r.Rest, o.OnAt + 1) = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GiftCount",
                table: "PointTransactions");

            migrationBuilder.DropColumn(
                name: "GiftId",
                table: "PointTransactions");

            migrationBuilder.DropColumn(
                name: "NovelId",
                table: "PointTransactions");

            migrationBuilder.DropColumn(
                name: "NameAr",
                table: "Gifts");
        }
    }
}
