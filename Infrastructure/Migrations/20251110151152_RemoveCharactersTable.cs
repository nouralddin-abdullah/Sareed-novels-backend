using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCharactersTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Characters");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Characters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NovelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CharacterAge = table.Column<int>(type: "int", nullable: false),
                    CharacterDescription = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: false),
                    CharacterImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CharacterName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Characters_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Characters_Novel_Name_Unique",
                table: "Characters",
                columns: new[] { "NovelId", "CharacterName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Characters_NovelId",
                table: "Characters",
                column: "NovelId");
        }
    }
}
