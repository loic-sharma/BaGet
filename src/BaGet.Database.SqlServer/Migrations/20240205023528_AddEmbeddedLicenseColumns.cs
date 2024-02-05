using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGet.Database.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddedLicenseColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasEmbeddedLicense",
                table: "Packages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LicenseIsMarkDown",
                table: "Packages",
                type: "bit",
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
        }
    }
}
