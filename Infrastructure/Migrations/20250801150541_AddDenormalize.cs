using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDenormalize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AverageCharacterDevelopmentScore",
                table: "Novels",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AverageUpdatingStabilityScore",
                table: "Novels",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AverageWorldBuildingScore",
                table: "Novels",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AverageWritingQualityScore",
                table: "Novels",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ReviewCount",
                table: "Novels",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalAverageScore",
                table: "Novels",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AverageCharacterDevelopmentScore",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "AverageUpdatingStabilityScore",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "AverageWorldBuildingScore",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "AverageWritingQualityScore",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "ReviewCount",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "TotalAverageScore",
                table: "Novels");
        }
    }
}
