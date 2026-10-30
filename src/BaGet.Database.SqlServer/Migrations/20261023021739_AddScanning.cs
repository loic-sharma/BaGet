using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGet.Database.SqlServer.Migrations
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
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ScanPolicies",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RepositoryKey = table.Column<int>(type: "int", nullable: true),
                    ProjectKey = table.Column<int>(type: "int", nullable: true),
                    WarnSeverity = table.Column<int>(type: "int", nullable: true),
                    BlockDownloadSeverity = table.Column<int>(type: "int", nullable: true),
                    BlockDeploySeverity = table.Column<int>(type: "int", nullable: true),
                    BlockUnscanned = table.Column<bool>(type: "bit", nullable: false),
                    Modified = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanPolicies", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ScanResults",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RepositoryKey = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Scanner = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ScannedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attempted = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Highest = table.Column<int>(type: "int", nullable: false),
                    FindingCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanResults", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ScanFindings",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ResultKey = table.Column<int>(type: "int", nullable: false),
                    VulnerabilityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Component = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ComponentVersion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AdvisoryUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FixedVersion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
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
                unique: true,
                filter: "[ProjectKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ScanPolicies_RepositoryKey",
                table: "ScanPolicies",
                column: "RepositoryKey",
                unique: true,
                filter: "[RepositoryKey] IS NOT NULL");

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
