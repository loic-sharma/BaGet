using Microsoft.EntityFrameworkCore.Migrations;

namespace BaGet.Database.PostgreSql.Migrations
{
    public partial class AddHasIconColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasIcon",
                table: "Packages",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasIcon",
                table: "Packages");
        }
    }
}
