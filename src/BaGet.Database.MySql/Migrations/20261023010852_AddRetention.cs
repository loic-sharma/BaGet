using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGet.Database.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArtifactUsages",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RepositoryKey = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Name = table.Column<string>(type: "varchar(340)", maxLength: 340, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastUsed = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtifactUsages", x => x.Key);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RetentionHolds",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RepositoryKey = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Name = table.Column<string>(type: "varchar(340)", maxLength: 340, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Created = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionHolds", x => x.Key);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RetentionPolicies",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RepositoryKey = table.Column<int>(type: "int", nullable: true),
                    ProjectKey = table.Column<int>(type: "int", nullable: true),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    NamePatterns = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    KeepLast = table.Column<int>(type: "int", nullable: true),
                    OlderThanDays = table.Column<int>(type: "int", nullable: true),
                    UnusedDays = table.Column<int>(type: "int", nullable: true),
                    PrereleaseOnly = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CacheLimitBytes = table.Column<long>(type: "bigint", nullable: true),
                    UntaggedAfterDays = table.Column<int>(type: "int", nullable: true),
                    CollectGarbage = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ScheduleHours = table.Column<int>(type: "int", nullable: true),
                    ScheduleStart = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastRun = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastResult = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Created = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Modified = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionPolicies", x => x.Key);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ArtifactUsages_RepositoryKey_Name_Version",
                table: "ArtifactUsages",
                columns: new[] { "RepositoryKey", "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RetentionHolds_RepositoryKey_Name_Version",
                table: "RetentionHolds",
                columns: new[] { "RepositoryKey", "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RetentionPolicies_Name",
                table: "RetentionPolicies",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RetentionPolicies_ProjectKey",
                table: "RetentionPolicies",
                column: "ProjectKey");

            migrationBuilder.CreateIndex(
                name: "IX_RetentionPolicies_RepositoryKey",
                table: "RetentionPolicies",
                column: "RepositoryKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArtifactUsages");

            migrationBuilder.DropTable(
                name: "RetentionHolds");

            migrationBuilder.DropTable(
                name: "RetentionPolicies");
        }
    }
}
