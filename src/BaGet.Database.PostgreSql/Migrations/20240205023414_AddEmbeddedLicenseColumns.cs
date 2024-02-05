using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGet.Database.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddedLicenseColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "Published",
                table: "Packages",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AddColumn<bool>(
                name: "HasEmbeddedLicense",
                table: "Packages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LicenseIsMarkDown",
                table: "Packages",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasEmbeddedLicense",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "LicenseIsMarkDown",
                table: "Packages");

            migrationBuilder.AlterColumn<DateTime>(
                name: "Published",
                table: "Packages",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");
        }
    }
}
