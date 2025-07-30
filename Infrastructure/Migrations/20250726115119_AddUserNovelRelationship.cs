using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserNovelRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthorId",
                table: "Novels",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Novels_AuthorId",
                table: "Novels",
                column: "AuthorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Novels_AspNetUsers_AuthorId",
                table: "Novels",
                column: "AuthorId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Novels_AspNetUsers_AuthorId",
                table: "Novels");

            migrationBuilder.DropIndex(
                name: "IX_Novels_AuthorId",
                table: "Novels");

            migrationBuilder.DropColumn(
                name: "AuthorId",
                table: "Novels");
        }
    }
}
