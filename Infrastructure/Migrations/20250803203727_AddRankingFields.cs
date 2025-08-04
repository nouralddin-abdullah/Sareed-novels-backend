using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRankingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NovelGenres_GenreId_GenreScore",
                table: "NovelGenres");

            migrationBuilder.AddColumn<bool>(
                name: "IsEligibleForRanking",
                table: "Novels",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastViewUpdate",
                table: "Novels",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "ViewsToday",
                table: "Novels",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "PopularityScore",
                table: "NovelGenres",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "QualityScore",
                table: "NovelGenres",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ReviewsLast30Days",
                table: "NovelGenres",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TrendingScore",
                table: "NovelGenres",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ViewsLast30Days",
                table: "NovelGenres",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ViewsLast7Days",
                table: "NovelGenres",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "NovelViews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NovelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ViewDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ViewCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NovelViews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NovelViews_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NovelGenres_GenreId_QualityScore",
                table: "NovelGenres",
                columns: new[] { "GenreId", "QualityScore" });

            migrationBuilder.CreateIndex(
                name: "IX_NovelGenres_GenreId_TrendingScore",
                table: "NovelGenres",
                columns: new[] { "GenreId", "TrendingScore" });

            migrationBuilder.CreateIndex(
                name: "IX_NovelViews_NovelId_ViewDate",
                table: "NovelViews",
                columns: new[] { "NovelId", "ViewDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NovelViews_ViewDate",
                table: "NovelViews",
                column: "ViewDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NovelViews");

            migrationBuilder.DropIndex(
                name: "IX_NovelGenres_GenreId_QualityScore",
                table: "NovelGenres");

            migrationBuilder.DropIndex(
                name: "IX_NovelGenres_GenreId_TrendingScore",
                table: "NovelGenres");

            migrationBuilder.DropColumn(
                name: "IsEligibleForRanking",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "LastViewUpdate",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "ViewsToday",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "PopularityScore",
                table: "NovelGenres");

            migrationBuilder.DropColumn(
                name: "QualityScore",
                table: "NovelGenres");

            migrationBuilder.DropColumn(
                name: "ReviewsLast30Days",
                table: "NovelGenres");

            migrationBuilder.DropColumn(
                name: "TrendingScore",
                table: "NovelGenres");

            migrationBuilder.DropColumn(
                name: "ViewsLast30Days",
                table: "NovelGenres");

            migrationBuilder.DropColumn(
                name: "ViewsLast7Days",
                table: "NovelGenres");

            migrationBuilder.CreateIndex(
                name: "IX_NovelGenres_GenreId_GenreScore",
                table: "NovelGenres",
                columns: new[] { "GenreId", "GenreScore" });
        }
    }
}
