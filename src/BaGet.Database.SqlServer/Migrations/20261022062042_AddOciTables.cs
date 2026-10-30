using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGet.Database.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddOciTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OciBlobs",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RepositoryKey = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Digest = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    RefCount = table.Column<int>(type: "int", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OciBlobs", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "OciRepositories",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RepositoryKey = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modified = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OciRepositories", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "OciBlobLinks",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImageKey = table.Column<int>(type: "int", nullable: false),
                    BlobKey = table.Column<int>(type: "int", nullable: false),
                    Manifests = table.Column<int>(type: "int", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OciBlobLinks", x => x.Key);
                    table.ForeignKey(
                        name: "FK_OciBlobLinks_OciBlobs_BlobKey",
                        column: x => x.BlobKey,
                        principalTable: "OciBlobs",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OciBlobLinks_OciRepositories_ImageKey",
                        column: x => x.ImageKey,
                        principalTable: "OciRepositories",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OciManifests",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImageKey = table.Column<int>(type: "int", nullable: false),
                    Digest = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    MediaType = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ArtifactType = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SubjectDigest = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OciManifests", x => x.Key);
                    table.ForeignKey(
                        name: "FK_OciManifests_OciRepositories_ImageKey",
                        column: x => x.ImageKey,
                        principalTable: "OciRepositories",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OciUploads",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImageKey = table.Column<int>(type: "int", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StorageUploadId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Updated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OciUploads", x => x.Key);
                    table.ForeignKey(
                        name: "FK_OciUploads_OciRepositories_ImageKey",
                        column: x => x.ImageKey,
                        principalTable: "OciRepositories",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OciTags",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImageKey = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ManifestKey = table.Column<int>(type: "int", nullable: false),
                    Updated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OciTags", x => x.Key);
                    table.ForeignKey(
                        name: "FK_OciTags_OciManifests_ManifestKey",
                        column: x => x.ManifestKey,
                        principalTable: "OciManifests",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OciBlobLinks_BlobKey",
                table: "OciBlobLinks",
                column: "BlobKey");

            migrationBuilder.CreateIndex(
                name: "IX_OciBlobLinks_ImageKey_BlobKey",
                table: "OciBlobLinks",
                columns: new[] { "ImageKey", "BlobKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OciBlobs_RepositoryKey_Digest",
                table: "OciBlobs",
                columns: new[] { "RepositoryKey", "Digest" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OciManifests_ImageKey_Digest",
                table: "OciManifests",
                columns: new[] { "ImageKey", "Digest" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OciManifests_ImageKey_SubjectDigest",
                table: "OciManifests",
                columns: new[] { "ImageKey", "SubjectDigest" });

            migrationBuilder.CreateIndex(
                name: "IX_OciRepositories_RepositoryKey_Name",
                table: "OciRepositories",
                columns: new[] { "RepositoryKey", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OciTags_ImageKey_Name",
                table: "OciTags",
                columns: new[] { "ImageKey", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OciTags_ManifestKey",
                table: "OciTags",
                column: "ManifestKey");

            migrationBuilder.CreateIndex(
                name: "IX_OciUploads_ImageKey",
                table: "OciUploads",
                column: "ImageKey");

            migrationBuilder.CreateIndex(
                name: "IX_OciUploads_Reference",
                table: "OciUploads",
                column: "Reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OciBlobLinks");

            migrationBuilder.DropTable(
                name: "OciTags");

            migrationBuilder.DropTable(
                name: "OciUploads");

            migrationBuilder.DropTable(
                name: "OciBlobs");

            migrationBuilder.DropTable(
                name: "OciManifests");

            migrationBuilder.DropTable(
                name: "OciRepositories");
        }
    }
}
