using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BaGet.Database.MySql.Migrations
{
    public partial class AddHasIconColumn : Migration
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
