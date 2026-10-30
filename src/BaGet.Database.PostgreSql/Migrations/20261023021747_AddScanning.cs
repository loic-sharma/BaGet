using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BaGet.Database.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddScanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "PackageVulnerabilities",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ScanPolicies",
                columns: table => new
                {
                    Key = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RepositoryKey = table.Column<int>(type: "integer", nullable: true),
                    ProjectKey = table.Column<int>(type: "integer", nullable: true),
                    WarnSeverity = table.Column<int>(type: "integer", nullable: true),
                    BlockDownloadSeverity = table.Column<int>(type: "integer", nullable: true),
                    BlockDeploySeverity = table.Column<int>(type: "integer", nullable: true),
                    BlockUnscanned = table.Column<bool>(type: "boolean", nullable: false),
                    Modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanPolicies", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ScanResults",
                columns: table => new
                {
                    Key = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RepositoryKey = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Version = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Scanner = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScannedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempted = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Highest = table.Column<int>(type: "integer", nullable: false),
                    FindingCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanResults", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ScanFindings",
                columns: table => new
                {
                    Key = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ResultKey = table.Column<int>(type: "integer", nullable: false),
                    VulnerabilityId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Component = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ComponentVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AdvisoryUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FixedVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanFindings", x => x.Key);
                    table.ForeignKey(
                        name: "FK_ScanFindings_ScanResults_ResultKey",
                        column: x => x.ResultKey,
                        principalTable: "ScanResults",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScanFindings_ResultKey",
                table: "ScanFindings",
                column: "ResultKey");

            migrationBuilder.CreateIndex(
                name: "IX_ScanPolicies_ProjectKey",
                table: "ScanPolicies",
                column: "ProjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScanPolicies_RepositoryKey",
                table: "ScanPolicies",
                column: "RepositoryKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScanResults_RepositoryKey_Name_Version",
                table: "ScanResults",
                columns: new[] { "RepositoryKey", "Name", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScanFindings");

            migrationBuilder.DropTable(
                name: "ScanPolicies");

            migrationBuilder.DropTable(
                name: "ScanResults");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "PackageVulnerabilities");
        }
    }
}
