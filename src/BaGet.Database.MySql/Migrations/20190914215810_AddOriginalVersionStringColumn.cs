using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BaGet.Database.MySql.Migrations
{
    public partial class AddOriginalVersionStringColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "RowVersion",
                table: "Packages",
                rowVersion: true,
                nullable: true,
                oldClrType: typeof(DateTime),
                oldNullable: true);

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

            migrationBuilder.AlterColumn<DateTime>(
                name: "RowVersion",
                table: "Packages",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldRowVersion: true,
                oldNullable: true);
        }
    }
}
