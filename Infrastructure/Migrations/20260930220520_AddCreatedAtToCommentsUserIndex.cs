using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// #54: every profile counts the member's listed comments (commentsCount), and their comment list pages them newest
    /// first. IX_Comments_UserId only found the member's rows; each then cost a lookup for the columns the list filters
    /// on, so for a member with many comments SQL Server scanned every comment instead, and since the query is
    /// parameterized, that plan could go on to serve every profile. Measured on 64,000 comments: the count read 196
    /// pages of Comments for a member with 60 comments (4 with this index); for one with 4,000 it scanned the whole
    /// table (1,682 pages), and a page of their list scanned and sorted it too (39 pages, in order, with this index).
    /// The index is keyed on (UserId, CreatedAt descending), which with the clustered Id is the list's order, and
    /// includes what the list filters on. It replaces IX_Comments_UserId, a prefix of it: every seek on UserId still
    /// has an index. Down puts that one back.
    /// </summary>
    public partial class AddCreatedAtToCommentsUserIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Comments_UserId",
                table: "Comments");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_UserId_CreatedAt",
                table: "Comments",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true })
                .Annotation("SqlServer:Include", new[] { "IsDeleted", "PostId", "ParentCommentId", "ChapterId", "ParagraphId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Comments_UserId_CreatedAt",
                table: "Comments");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_UserId",
                table: "Comments",
                column: "UserId");
        }
    }
}
