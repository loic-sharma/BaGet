using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGet.Database.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddMavenTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MavenArtifacts",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RepositoryKey = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    GroupId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ArtifactId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Modified = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MavenArtifacts", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "MavenVersions",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ArtifactKey = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IsSnapshot = table.Column<bool>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Packaging = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Modified = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MavenVersions", x => x.Key);
                    table.ForeignKey(
                        name: "FK_MavenVersions_MavenArtifacts_ArtifactKey",
                        column: x => x.ArtifactKey,
                        principalTable: "MavenArtifacts",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MavenFiles",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VersionKey = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Classifier = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Extension = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SnapshotTimestamp = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    SnapshotBuild = table.Column<int>(type: "INTEGER", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha1 = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Md5 = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Sha512 = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MavenFiles", x => x.Key);
                    table.ForeignKey(
                        name: "FK_MavenFiles_MavenVersions_VersionKey",
                        column: x => x.VersionKey,
                        principalTable: "MavenVersions",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MavenArtifacts_RepositoryKey_GroupId_ArtifactId",
                table: "MavenArtifacts",
                columns: new[] { "RepositoryKey", "GroupId", "ArtifactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MavenFiles_VersionKey_FileName",
                table: "MavenFiles",
                columns: new[] { "VersionKey", "FileName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MavenVersions_ArtifactKey_Version",
                table: "MavenVersions",
                columns: new[] { "ArtifactKey", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MavenFiles");

            migrationBuilder.DropTable(
                name: "MavenVersions");

            migrationBuilder.DropTable(
                name: "MavenArtifacts");
        }
    }
}
