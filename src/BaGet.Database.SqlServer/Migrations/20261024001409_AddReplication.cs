using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGet.Database.SqlServer.Migrations
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
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RepositoryKey = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    PeerUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    PeerRepository = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ProtectedApiKey = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    OnDeploy = table.Column<bool>(type: "bit", nullable: false),
                    ScheduleMinutes = table.Column<int>(type: "int", nullable: true),
                    PropagateDeletes = table.Column<bool>(type: "bit", nullable: false),
                    Conflict = table.Column<int>(type: "int", nullable: false),
                    ScheduleStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastRun = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastResult = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modified = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplicationConfigs", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ReplicationItems",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConfigKey = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(340)", maxLength: 340, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Part = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Digest = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Synced = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplicationItems", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ReplicationRuns",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConfigKey = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Trigger = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Actor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ResumedFrom = table.Column<int>(type: "int", nullable: true),
                    Started = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Finished = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Examined = table.Column<int>(type: "int", nullable: false),
                    Transferred = table.Column<int>(type: "int", nullable: false),
                    Skipped = table.Column<int>(type: "int", nullable: false),
                    Deleted = table.Column<int>(type: "int", nullable: false),
                    Conflicts = table.Column<int>(type: "int", nullable: false),
                    Failures = table.Column<int>(type: "int", nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
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
