using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;

namespace BaGet.Migrations.Sqlite
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
                    Authors = table.Column<string>(maxLength: 4000, nullable: true),
                    Description = table.Column<string>(maxLength: 4000, nullable: true),
                    Downloads = table.Column<long>(nullable: false),
                    HasReadme = table.Column<bool>(nullable: false),
                    IconUrl = table.Column<string>(maxLength: 4000, nullable: true),
                    Id = table.Column<string>(type: "TEXT COLLATE NOCASE", maxLength: 128, nullable: true),
                    Language = table.Column<string>(maxLength: 20, nullable: true),
                    LicenseUrl = table.Column<string>(maxLength: 4000, nullable: true),
                    Listed = table.Column<bool>(nullable: false),
                    MinClientVersion = table.Column<string>(maxLength: 44, nullable: true),
                    ProjectUrl = table.Column<string>(maxLength: 4000, nullable: true),
                    Published = table.Column<DateTime>(nullable: false),
                    RequireLicenseAcceptance = table.Column<bool>(nullable: false),
                    Summary = table.Column<string>(maxLength: 4000, nullable: true),
                    Tags = table.Column<string>(maxLength: 4000, nullable: true),
                    Title = table.Column<string>(maxLength: 256, nullable: true),
                    Version = table.Column<string>(maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Packages", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "PackageDependency",
                columns: table => new
                {
                    Key = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Id = table.Column<string>(type: "TEXT COLLATE NOCASE", maxLength: 128, nullable: true),
                    PackageKey = table.Column<int>(nullable: true),
                    TargetFramework = table.Column<string>(maxLength: 256, nullable: true),
                    VersionRange = table.Column<string>(maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageDependency", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PackageDependency_Packages_PackageKey",
                        column: x => x.PackageKey,
                        principalTable: "Packages",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackageDependency_PackageKey",
                table: "PackageDependency",
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
                name: "PackageDependency");

            migrationBuilder.DropTable(
                name: "Packages");
        }
    }
}
