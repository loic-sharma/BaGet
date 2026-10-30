using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGet.Database.Sqlite.Migrations
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
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RepositoryKey = table.Column<int>(type: "INTEGER", nullable: false),
                    Direction = table.Column<int>(type: "INTEGER", nullable: false),
                    PeerUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    PeerRepository = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ProtectedApiKey = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    OnDeploy = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScheduleMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    PropagateDeletes = table.Column<bool>(type: "INTEGER", nullable: false),
                    Conflict = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduleStart = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastRun = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastResult = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Modified = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplicationConfigs", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ReplicationItems",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConfigKey = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 340, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Part = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Digest = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    Synced = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplicationItems", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ReplicationRuns",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConfigKey = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ResumedFrom = table.Column<int>(type: "INTEGER", nullable: true),
                    Started = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Finished = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Examined = table.Column<int>(type: "INTEGER", nullable: false),
                    Transferred = table.Column<int>(type: "INTEGER", nullable: false),
                    Skipped = table.Column<int>(type: "INTEGER", nullable: false),
                    Deleted = table.Column<int>(type: "INTEGER", nullable: false),
                    Conflicts = table.Column<int>(type: "INTEGER", nullable: false),
                    Failures = table.Column<int>(type: "INTEGER", nullable: false),
                    Bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
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
