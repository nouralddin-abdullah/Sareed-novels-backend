using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyEntityToSectionBased : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategoryName",
                table: "NovelEntities");

            migrationBuilder.RenameColumn(
                name: "EntityType",
                table: "NovelEntities",
                newName: "Section");

            migrationBuilder.RenameIndex(
                name: "IX_NovelEntities_Novel_Type",
                table: "NovelEntities",
                newName: "IX_NovelEntities_Novel_Section");

            migrationBuilder.RenameIndex(
                name: "IX_NovelEntities_EntityType",
                table: "NovelEntities",
                newName: "IX_NovelEntities_Section");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Section",
                table: "NovelEntities",
                newName: "EntityType");

            migrationBuilder.RenameIndex(
                name: "IX_NovelEntities_Section",
                table: "NovelEntities",
                newName: "IX_NovelEntities_EntityType");

            migrationBuilder.RenameIndex(
                name: "IX_NovelEntities_Novel_Section",
                table: "NovelEntities",
                newName: "IX_NovelEntities_Novel_Type");

            migrationBuilder.AddColumn<string>(
                name: "CategoryName",
                table: "NovelEntities",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
