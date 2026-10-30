using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BaGet.Database.PostgreSql.Migrations
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
                    Key = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RepositoryKey = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Name = table.Column<string>(type: "character varying(340)", maxLength: 340, nullable: false),
                    Version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LastUsed = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtifactUsages", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "RetentionHolds",
                columns: table => new
                {
                    Key = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RepositoryKey = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Name = table.Column<string>(type: "character varying(340)", maxLength: 340, nullable: false),
                    Version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionHolds", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "RetentionPolicies",
                columns: table => new
                {
                    Key = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RepositoryKey = table.Column<int>(type: "integer", nullable: true),
                    ProjectKey = table.Column<int>(type: "integer", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    NamePatterns = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    KeepLast = table.Column<int>(type: "integer", nullable: true),
                    OlderThanDays = table.Column<int>(type: "integer", nullable: true),
                    UnusedDays = table.Column<int>(type: "integer", nullable: true),
                    PrereleaseOnly = table.Column<bool>(type: "boolean", nullable: false),
                    CacheLimitBytes = table.Column<long>(type: "bigint", nullable: true),
                    UntaggedAfterDays = table.Column<int>(type: "integer", nullable: true),
                    CollectGarbage = table.Column<bool>(type: "boolean", nullable: false),
                    ScheduleHours = table.Column<int>(type: "integer", nullable: true),
                    ScheduleStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRun = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastResult = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionPolicies", x => x.Key);
                });

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
