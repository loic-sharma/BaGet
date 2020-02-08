using Microsoft.EntityFrameworkCore.Migrations;

namespace BaGet.Database.Sqlite.Migrations
{
    public partial class FixVersionCaseSensitivity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Disable this migration as SQLite does not support altering columns.
            // Customers will need to create a new database and reupload their packages.
            // See: https://docs.microsoft.com/en-us/ef/core/providers/sqlite/limitations#migrations-limitations
            //migrationBuilder.AlterColumn<string>(
            //    name: "Version",
            //    table: "Packages",
            //    type: "TEXT COLLATE NOCASE",
            //    maxLength: 64,
            //    nullable: false,
            //    oldClrType: typeof(string),
            //    oldMaxLength: 64);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            //migrationBuilder.AlterColumn<string>(
            //    name: "Version",
            //    table: "Packages",
            //    maxLength: 64,
            //    nullable: false,
            //    oldClrType: typeof(string),
            //    oldType: "TEXT COLLATE NOCASE",
            //    oldMaxLength: 64);
        }
    }
}
