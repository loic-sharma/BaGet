using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BaGet.Database.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddReplication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReplicationConfigs",
                columns: table => new
                {
                    Key = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RepositoryKey = table.Column<int>(type: "integer", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    PeerUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    PeerRepository = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProtectedApiKey = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    OnDeploy = table.Column<bool>(type: "boolean", nullable: false),
                    ScheduleMinutes = table.Column<int>(type: "integer", nullable: true),
                    PropagateDeletes = table.Column<bool>(type: "boolean", nullable: false),
                    Conflict = table.Column<int>(type: "integer", nullable: false),
                    ScheduleStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRun = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastResult = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplicationConfigs", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ReplicationItems",
                columns: table => new
                {
                    Key = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConfigKey = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(340)", maxLength: 340, nullable: false),
                    Version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Part = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Digest = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Synced = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplicationItems", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ReplicationRuns",
                columns: table => new
                {
                    Key = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConfigKey = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ResumedFrom = table.Column<int>(type: "integer", nullable: true),
                    Started = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Finished = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Examined = table.Column<int>(type: "integer", nullable: false),
                    Transferred = table.Column<int>(type: "integer", nullable: false),
                    Skipped = table.Column<int>(type: "integer", nullable: false),
                    Deleted = table.Column<int>(type: "integer", nullable: false),
                    Conflicts = table.Column<int>(type: "integer", nullable: false),
                    Failures = table.Column<int>(type: "integer", nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    Error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Detail = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplicationRuns", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReplicationConfigs_Name",
                table: "ReplicationConfigs",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReplicationConfigs_RepositoryKey",
                table: "ReplicationConfigs",
                column: "RepositoryKey");

            migrationBuilder.CreateIndex(
                name: "IX_ReplicationItems_ConfigKey_Name_Version_Part",
                table: "ReplicationItems",
                columns: new[] { "ConfigKey", "Name", "Version", "Part" });

            migrationBuilder.CreateIndex(
                name: "IX_ReplicationRuns_ConfigKey_Started",
                table: "ReplicationRuns",
                columns: new[] { "ConfigKey", "Started" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReplicationConfigs");

            migrationBuilder.DropTable(
                name: "ReplicationItems");

            migrationBuilder.DropTable(
                name: "ReplicationRuns");
        }
    }
}
