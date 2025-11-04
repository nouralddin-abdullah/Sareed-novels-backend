using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContentHashToParagraphs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add column with default empty string
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "ChapterParagraphs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Create index
            migrationBuilder.CreateIndex(
                name: "IX_ChapterParagraphs_ContentHash",
                table: "ChapterParagraphs",
                column: "ContentHash");
            
            // NOTE: Existing paragraphs will have empty ContentHash
            // They will be treated as changed on first edit (all recreated)
            // This is acceptable - a one-time migration effect
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChapterParagraphs_ContentHash",
                table: "ChapterParagraphs");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "ChapterParagraphs");
        }
    }
}
