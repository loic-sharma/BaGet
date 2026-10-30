using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGet.Database.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddNpmTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NpmPackages",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RepositoryKey = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Name = table.Column<string>(type: "nvarchar(214)", maxLength: 214, nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modified = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NpmPackages", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "NpmDistTags",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackageKey = table.Column<int>(type: "int", nullable: false),
                    Tag = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NpmDistTags", x => x.Key);
                    table.ForeignKey(
                        name: "FK_NpmDistTags_NpmPackages_PackageKey",
                        column: x => x.PackageKey,
                        principalTable: "NpmPackages",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NpmVersions",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackageKey = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Integrity = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Shasum = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TarballSize = table.Column<long>(type: "bigint", nullable: false),
                    Manifest = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Deprecated = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Published = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NpmVersions", x => x.Key);
                    table.ForeignKey(
                        name: "FK_NpmVersions_NpmPackages_PackageKey",
                        column: x => x.PackageKey,
                        principalTable: "NpmPackages",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NpmDistTags_PackageKey_Tag",
                table: "NpmDistTags",
                columns: new[] { "PackageKey", "Tag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NpmPackages_RepositoryKey_Name",
                table: "NpmPackages",
                columns: new[] { "RepositoryKey", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NpmVersions_PackageKey_Version",
                table: "NpmVersions",
                columns: new[] { "PackageKey", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NpmDistTags");

            migrationBuilder.DropTable(
                name: "NpmVersions");

            migrationBuilder.DropTable(
                name: "NpmPackages");
        }
    }
}
