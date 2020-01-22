using Microsoft.EntityFrameworkCore.Migrations;

namespace BaGet.Database.Sqlite.Migrations
{
    public partial class AddReleaseNotesStringColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReleaseNotes",
                table: "Packages",
                maxLength: 4000,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReleaseNotes",
                table: "Packages");
        }
    }
}
