using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Competitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TotalPrize = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PrizeFirstPlace = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PrizeSecondPlace = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PrizeThirdPlace = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ParticipationStartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ParticipationEndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    JudgmentStartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    JudgmentEndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResultsDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MaxNovelAgeDays = table.Column<int>(type: "int", nullable: true),
                    MinChapters = table.Column<int>(type: "int", nullable: false, defaultValue: 5),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Upcoming"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Competitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionParticipants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NovelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ViewsAtJoin = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CurrentPoints = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    ExtraPoints = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    CurrentRank = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionParticipants_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompetitionParticipants_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CompetitionWinners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NovelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    FinalPoints = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FinalViews = table.Column<int>(type: "int", nullable: false),
                    PrizeWon = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AwardedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionWinners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionWinners_AspNetUsers_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CompetitionWinners_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompetitionWinners_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionParticipants_Competition_Novel_Unique",
                table: "CompetitionParticipants",
                columns: new[] { "CompetitionId", "NovelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionParticipants_Competition_Points",
                table: "CompetitionParticipants",
                columns: new[] { "CompetitionId", "CurrentPoints" });

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionParticipants_CompetitionId",
                table: "CompetitionParticipants",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionParticipants_JoinedAt",
                table: "CompetitionParticipants",
                column: "JoinedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionParticipants_NovelId",
                table: "CompetitionParticipants",
                column: "NovelId");

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_Active_Status",
                table: "Competitions",
                columns: new[] { "IsActive", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_ParticipationStart",
                table: "Competitions",
                column: "ParticipationStartDate");

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_Slug_Unique",
                table: "Competitions",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_Status",
                table: "Competitions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionWinners_AuthorId",
                table: "CompetitionWinners",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionWinners_Competition_Rank",
                table: "CompetitionWinners",
                columns: new[] { "CompetitionId", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionWinners_CompetitionId",
                table: "CompetitionWinners",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionWinners_NovelId",
                table: "CompetitionWinners",
                column: "NovelId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompetitionParticipants");

            migrationBuilder.DropTable(
                name: "CompetitionWinners");

            migrationBuilder.DropTable(
                name: "Competitions");
        }
    }
}
