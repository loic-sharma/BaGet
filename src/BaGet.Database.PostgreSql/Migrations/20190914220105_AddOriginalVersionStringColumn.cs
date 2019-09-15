using Microsoft.EntityFrameworkCore.Migrations;

namespace BaGet.Database.PostgreSql.Migrations
{
    public partial class AddOriginalVersionStringColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OriginalVersion",
                table: "Packages",
                maxLength: 64,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalVersion",
                table: "Packages");
        }
    }
}
