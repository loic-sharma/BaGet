using Microsoft.EntityFrameworkCore.Migrations;

namespace BaGet.Database.PostgreSql.Migrations
{
    public partial class ReleaseNotesColumnLength : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ReleaseNotes",
                table: "Packages",
                maxLength: 35000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ReleaseNotes",
                table: "Packages",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldMaxLength: 35000,
                oldNullable: true);
        }
    }
}
