using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// Account security (issue #16):
    /// 1. AspNetUsers.TokensValidAfter, the sign-out-everywhere cut-off. Null for everyone, so nobody is signed out.
    /// 2. UserNameChanges: user names members gave up, so links to an old name (/profile/{old}) still find them.
    /// 3. Data fix: user names containing "@" (email addresses typed as user names, published as @handles and in
    ///    profile links) become a handle made from the address's local part: lower case, only a-z 0-9 . _ -, no
    ///    punctuation at either end, 3 to 20 characters, "sarduser" + 6 digits when less than 3 are left, and 2, 3, ...
    ///    appended when the handle is taken. The old names go to UserNameChanges; notification links to the old
    ///    profile URL, and gift descriptions in authors' wallets that named the sender by such a name, are rewritten.
    ///    Deterministic (oldest account first) and idempotent: once it ran, no user name has an "@" and it does nothing.
    /// Profile photos stored under keys with the old name are left as they are (R2 objects aren't moved).
    /// </summary>
    public partial class ReplaceEmailUserNamesAndRevokeTokens : Migration
    {
        internal const string ReplaceEmailUserNamesSql = """
            SET NOCOUNT ON;

            DECLARE @Renames TABLE (
                Seq int IDENTITY(1, 1) PRIMARY KEY,
                UserId nvarchar(450) NOT NULL,
                OldUserName nvarchar(256) NOT NULL,
                OldNormalizedUserName nvarchar(256) NOT NULL,
                Handle nvarchar(256) NOT NULL DEFAULT N'',
                NewUserName nvarchar(256) NULL);

            -- Oldest account first: it keeps the plain handle when two addresses share a local part.
            INSERT INTO @Renames (UserId, OldUserName, OldNormalizedUserName)
            SELECT Id, UserName, COALESCE(NormalizedUserName, UPPER(UserName))
            FROM AspNetUsers
            WHERE UserName LIKE N'%@%'
            ORDER BY CreatedAt, Id;

            -- The local part (before the first "@") in lower case, keeping only a-z 0-9 . _ - (by code point, so no
            -- collation decides what counts as a letter).
            WITH Digits AS (SELECT d FROM (VALUES (0), (1), (2), (3), (4), (5), (6), (7), (8), (9)) AS v(d)),
            Positions AS (SELECT a.d * 100 + b.d * 10 + c.d + 1 AS i FROM Digits a CROSS JOIN Digits b CROSS JOIN Digits c)
            UPDATE r
            SET Handle = ISNULL((
                SELECT CASE
                           WHEN ch.code BETWEEN 65 AND 90 THEN NCHAR(ch.code + 32)
                           WHEN ch.code BETWEEN 97 AND 122 OR ch.code BETWEEN 48 AND 57 OR ch.code IN (45, 46, 95) THEN NCHAR(ch.code)
                           ELSE N''
                       END
                FROM Positions p
                CROSS APPLY (SELECT UNICODE(SUBSTRING(r.OldUserName, p.i, 1)) AS code) ch
                WHERE p.i < CHARINDEX(N'@', r.OldUserName)
                ORDER BY p.i
                FOR XML PATH(''), TYPE).value(N'.', N'nvarchar(256)'), N'')
            FROM @Renames r;

            -- No ".", "_" or "-" at either end (a dot at the end of a URL breaks on some servers), at most 20 characters.
            UPDATE @Renames SET Handle = N'' WHERE PATINDEX(N'%[^._-]%', Handle) = 0;
            UPDATE @Renames SET Handle = LEFT(SUBSTRING(Handle, PATINDEX(N'%[^._-]%', Handle), 256), 20) WHERE Handle <> N'';
            UPDATE @Renames SET Handle = LEFT(Handle, LEN(Handle) - PATINDEX(N'%[^._-]%', REVERSE(Handle)) + 1) WHERE Handle <> N'';

            -- Too little left: "sarduser" + 6 digits derived from the user id, like the handles of Google sign-ups.
            UPDATE @Renames
            SET Handle = N'sarduser' + RIGHT(N'00000' + CONVERT(nvarchar(10),
                CONVERT(bigint, CONVERT(binary(4), HASHBYTES('SHA2_256', UserId))) % 1000000), 6)
            WHERE LEN(Handle) < 3;

            -- Unique among all user names (Identity matches the upper-case normalized name) and within this batch:
            -- append 2, 3, ... keeping 20 characters at most.
            DECLARE @Seq int = 1, @Last int = (SELECT COUNT(*) FROM @Renames);
            DECLARE @Handle nvarchar(256), @Candidate nvarchar(256), @Suffix nvarchar(10), @N int;
            WHILE @Seq <= @Last
            BEGIN
                SELECT @Handle = Handle FROM @Renames WHERE Seq = @Seq;
                SELECT @Candidate = @Handle, @N = 1;
                WHILE EXISTS (SELECT 1 FROM AspNetUsers WHERE NormalizedUserName = UPPER(@Candidate) OR UPPER(UserName) = UPPER(@Candidate))
                   OR EXISTS (SELECT 1 FROM @Renames WHERE UPPER(NewUserName) = UPPER(@Candidate))
                BEGIN
                    SET @N = @N + 1;
                    SET @Suffix = CONVERT(nvarchar(10), @N);
                    SET @Candidate = LEFT(@Handle, 20 - LEN(@Suffix)) + @Suffix;
                END;
                UPDATE @Renames SET NewUserName = @Candidate WHERE Seq = @Seq;
                SET @Seq = @Seq + 1;
            END;

            DECLARE @Now datetime2 = SYSUTCDATETIME();

            -- Old profile links keep working.
            INSERT INTO UserNameChanges (UserId, OldUserName, OldNormalizedUserName, ChangedAt)
            SELECT UserId, OldUserName, OldNormalizedUserName, @Now
            FROM @Renames
            ORDER BY Seq;

            -- Notifications link to a profile by user name (a new follower, a comment on a post).
            UPDATE n
            SET ActionUrl = N'/profile/' + r.NewUserName
            FROM Notifications n
            JOIN @Renames r ON n.ActionUrl = N'/profile/' + r.OldUserName;

            -- Authors' wallets named gift senders by user name ("Received 2x Rose from <user name> on <novel>"): now by
            -- display name, as new gifts do (the new handle if the display name is empty or has an "@" as well).
            UPDATE t
            SET Description = LEFT(REPLACE(t.Description, N' from ' + s.OldUserName + N' on ', N' from ' + s.Shown + N' on '), 500)
            FROM PointTransactions t
            JOIN (
                SELECT r.OldUserName,
                       CASE WHEN LTRIM(RTRIM(ISNULL(u.DisplayName, N''))) = N'' OR u.DisplayName LIKE N'%@%'
                            THEN r.NewUserName ELSE u.DisplayName END AS Shown
                FROM @Renames r
                JOIN AspNetUsers u ON u.Id = r.UserId
            ) s ON CHARINDEX(N' from ' + s.OldUserName + N' on ', t.Description) > 0
            WHERE t.Type = N'GiftReceived' AND t.Description LIKE N'%@%';

            -- The renames. SearchName is refilled from the new name at startup (BackfillSearchColumnsAsync).
            UPDATE u
            SET UserName = r.NewUserName,
                NormalizedUserName = UPPER(r.NewUserName),
                SearchName = N'',
                ConcurrencyStamp = CONVERT(nvarchar(36), NEWID())
            FROM AspNetUsers u
            JOIN @Renames r ON r.UserId = u.Id;
            """;

        /// <summary>
        /// Puts back the names the data fix replaced (the only recorded old names with an "@": user names can't have
        /// one since) unless someone else holds them now, and the notification links to them. Gift descriptions keep
        /// the display name. A member who renamed themselves after the fix gets the old name back too.
        /// </summary>
        internal const string RestoreEmailUserNamesSql = """
            SET NOCOUNT ON;

            DECLARE @Restores TABLE (
                UserId nvarchar(450) NOT NULL PRIMARY KEY,
                OldUserName nvarchar(256) NOT NULL,
                OldNormalizedUserName nvarchar(256) NOT NULL,
                CurrentUserName nvarchar(256) NOT NULL);

            INSERT INTO @Restores (UserId, OldUserName, OldNormalizedUserName, CurrentUserName)
            SELECT c.UserId, c.OldUserName, c.OldNormalizedUserName, u.UserName
            FROM UserNameChanges c
            JOIN AspNetUsers u ON u.Id = c.UserId
            WHERE c.Id = (SELECT MIN(f.Id) FROM UserNameChanges f WHERE f.UserId = c.UserId AND f.OldUserName LIKE N'%@%')
              AND u.UserName NOT LIKE N'%@%'
              AND NOT EXISTS (SELECT 1 FROM AspNetUsers o WHERE o.NormalizedUserName = c.OldNormalizedUserName AND o.Id <> c.UserId);

            UPDATE n
            SET ActionUrl = N'/profile/' + r.OldUserName
            FROM Notifications n
            JOIN @Restores r ON n.ActionUrl = N'/profile/' + r.CurrentUserName;

            UPDATE u
            SET UserName = r.OldUserName,
                NormalizedUserName = r.OldNormalizedUserName,
                SearchName = N'',
                ConcurrencyStamp = CONVERT(nvarchar(36), NEWID())
            FROM AspNetUsers u
            JOIN @Restores r ON r.UserId = u.Id;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TokensValidAfter",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserNameChanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OldUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OldNormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNameChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNameChanges_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserNameChanges_OldNormalizedUserName",
                table: "UserNameChanges",
                column: "OldNormalizedUserName");

            migrationBuilder.CreateIndex(
                name: "IX_UserNameChanges_UserId",
                table: "UserNameChanges",
                column: "UserId");

            migrationBuilder.Sql(ReplaceEmailUserNamesSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RestoreEmailUserNamesSql);

            migrationBuilder.DropTable(
                name: "UserNameChanges");

            migrationBuilder.DropColumn(
                name: "TokensValidAfter",
                table: "AspNetUsers");
        }
    }
}
