using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <summary>
    /// #45: the library page counts each novel's chapters published after the reader's last read (newChaptersCount),
    /// next to MAX(PublishedAt) (lastChapterPublishedAt). No index had PublishedAt, so SQL Server read every chapter of
    /// the platform for each item of the page (or spooled all of them into a temporary index on every request): at
    /// production's size (62 novels, 1,296 chapters) a 20-item page went from about 2-3 ms to 8 ms, a 100-item page to
    /// 20 ms, growing with every chapter published (300 ms at 9,000 chapters). The (NovelId, Status) index, a prefix of
    /// IX_Chapters_Novel_Status_Index and so of no use of its own, gains PublishedAt: both are then seeks, 1.5-2 ms a
    /// page at that size. Every seek on (NovelId, Status) still has an index. Down puts the two-column index back.
    /// </summary>
    public partial class AddPublishedAtToChapterNovelStatusIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Chapters_Novel_Status",
                table: "Chapters");

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_Novel_Status_PublishedAt",
                table: "Chapters",
                columns: new[] { "NovelId", "Status", "PublishedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Chapters_Novel_Status_PublishedAt",
                table: "Chapters");

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_Novel_Status",
                table: "Chapters",
                columns: new[] { "NovelId", "Status" });
        }
    }
}
