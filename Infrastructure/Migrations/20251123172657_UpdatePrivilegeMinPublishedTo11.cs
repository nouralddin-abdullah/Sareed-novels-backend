using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePrivilegeMinPublishedTo11 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "MinPublishedRequired",
                table: "NovelPrivileges",
                type: "int",
                nullable: false,
                defaultValue: 11,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "MinPublishedRequired",
                table: "NovelPrivileges",
                type: "int",
                nullable: false,
                defaultValue: 10,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 11);
        }
    }
}
