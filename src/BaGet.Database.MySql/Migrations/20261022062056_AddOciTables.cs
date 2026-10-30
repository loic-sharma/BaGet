using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGet.Database.MySql.Migrations
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
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RepositoryKey = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Digest = table.Column<string>(type: "varchar(140)", maxLength: 140, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    RefCount = table.Column<int>(type: "int", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OciBlobs", x => x.Key);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OciRepositories",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RepositoryKey = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Created = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Modified = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OciRepositories", x => x.Key);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OciBlobLinks",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ImageKey = table.Column<int>(type: "int", nullable: false),
                    BlobKey = table.Column<int>(type: "int", nullable: false),
                    Manifests = table.Column<int>(type: "int", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime(6)", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OciManifests",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ImageKey = table.Column<int>(type: "int", nullable: false),
                    Digest = table.Column<string>(type: "varchar(140)", maxLength: 140, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MediaType = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ArtifactType = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubjectDigest = table.Column<string>(type: "varchar(140)", maxLength: 140, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Content = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Created = table.Column<DateTime>(type: "datetime(6)", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OciUploads",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ImageKey = table.Column<int>(type: "int", nullable: false),
                    Reference = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StorageUploadId = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Created = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Updated = table.Column<DateTime>(type: "datetime(6)", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OciTags",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ImageKey = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ManifestKey = table.Column<int>(type: "int", nullable: false),
                    Updated = table.Column<DateTime>(type: "datetime(6)", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

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
