using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;

namespace BaGet.Core.Migrations
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
                    Authors = table.Column<string>(nullable: true),
                    Description = table.Column<string>(nullable: true),
                    IconUrl = table.Column<string>(nullable: true),
                    Id = table.Column<string>(type: "TEXT COLLATE NOCASE", nullable: true),
                    LicenseUrl = table.Column<string>(nullable: true),
                    Listed = table.Column<bool>(nullable: false),
                    MinClientVersion = table.Column<string>(nullable: true),
                    ProjectUrl = table.Column<string>(nullable: true),
                    Published = table.Column<DateTime>(nullable: false),
                    RequireLicenseAcceptance = table.Column<bool>(nullable: false),
                    Summary = table.Column<string>(nullable: true),
                    Tags = table.Column<string>(nullable: true),
                    Title = table.Column<string>(nullable: true),
                    Version = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Packages", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "PackageDependencyGroup",
                columns: table => new
                {
                    Key = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PackageKey = table.Column<int>(nullable: true),
                    TargetFramework = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageDependencyGroup", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PackageDependencyGroup_Packages_PackageKey",
                        column: x => x.PackageKey,
                        principalTable: "Packages",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PackageDependency",
                columns: table => new
                {
                    Key = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Id = table.Column<string>(nullable: true),
                    PackageDependencyGroupKey = table.Column<int>(nullable: true),
                    VersionRange = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageDependency", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PackageDependency_PackageDependencyGroup_PackageDependencyGroupKey",
                        column: x => x.PackageDependencyGroupKey,
                        principalTable: "PackageDependencyGroup",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackageDependency_PackageDependencyGroupKey",
                table: "PackageDependency",
                column: "PackageDependencyGroupKey");

            migrationBuilder.CreateIndex(
                name: "IX_PackageDependencyGroup_PackageKey",
                table: "PackageDependencyGroup",
                column: "PackageKey");

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
                name: "PackageDependencyGroup");

            migrationBuilder.DropTable(
                name: "Packages");
        }
    }
}
