using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BaGet.Database.Sqlite.Migrations
{
    public partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Packages",
                columns: table => new
                {
                    Key = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Id = table.Column<string>(type: "TEXT COLLATE NOCASE", maxLength: 128, nullable: false),
                    Authors = table.Column<string>(maxLength: 4000, nullable: true),
                    Description = table.Column<string>(maxLength: 4000, nullable: true),
                    Downloads = table.Column<long>(nullable: false),
                    HasReadme = table.Column<bool>(nullable: false),
                    Language = table.Column<string>(maxLength: 20, nullable: true),
                    Listed = table.Column<bool>(nullable: false),
                    MinClientVersion = table.Column<string>(maxLength: 44, nullable: true),
                    Published = table.Column<DateTime>(nullable: false),
                    RequireLicenseAcceptance = table.Column<bool>(nullable: false),
                    Summary = table.Column<string>(maxLength: 4000, nullable: true),
                    Title = table.Column<string>(maxLength: 256, nullable: true),
                    IconUrl = table.Column<string>(maxLength: 4000, nullable: true),
                    LicenseUrl = table.Column<string>(maxLength: 4000, nullable: true),
                    ProjectUrl = table.Column<string>(maxLength: 4000, nullable: true),
                    RepositoryUrl = table.Column<string>(maxLength: 4000, nullable: true),
                    RepositoryType = table.Column<string>(maxLength: 100, nullable: true),
                    Tags = table.Column<string>(maxLength: 4000, nullable: true),
                    RowVersion = table.Column<byte[]>(rowVersion: true, nullable: true),

                    // HACK - This migration was retroactively modified by: https://github.com/loic-sharma/BaGet/pull/466
                    // The version column used to be case sensitive:
                    //   Version = table.Column<string>(maxLength: 64, nullable: false)
                    // This hack is necessary as SQLite cannot alter columns.
                    // See: https://docs.microsoft.com/en-us/ef/core/providers/sqlite/limitations#migrations-limitations
                    Version = table.Column<string>(type: "TEXT COLLATE NOCASE", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Packages", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "PackageDependencies",
                columns: table => new
                {
                    Key = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Id = table.Column<string>(type: "TEXT COLLATE NOCASE", maxLength: 128, nullable: true),
                    VersionRange = table.Column<string>(maxLength: 256, nullable: true),
                    TargetFramework = table.Column<string>(maxLength: 256, nullable: true),
                    PackageKey = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageDependencies", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PackageDependencies_Packages_PackageKey",
                        column: x => x.PackageKey,
                        principalTable: "Packages",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackageDependencies_PackageKey",
                table: "PackageDependencies",
                column: "PackageKey");

            migrationBuilder.CreateIndex(
                name: "IX_Packages_Id",
                table: "Packages",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Packages_Id_Version",
                table: "Packages",
                columns: new[] { "Id", "Version" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackageDependencies");

            migrationBuilder.DropTable(
                name: "Packages");
        }
    }
}
