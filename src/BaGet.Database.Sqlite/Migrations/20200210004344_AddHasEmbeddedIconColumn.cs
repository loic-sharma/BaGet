using Microsoft.EntityFrameworkCore.Migrations;

namespace BaGet.Database.Sqlite.Migrations
{
    public partial class AddHasEmbeddedIconColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasEmbeddedIcon",
                table: "Packages",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasEmbeddedIcon",
                table: "Packages");
        }
    }
}
