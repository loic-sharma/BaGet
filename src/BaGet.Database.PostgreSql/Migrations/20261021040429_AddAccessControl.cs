using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BaGet.Database.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessAuditEntries",
                columns: table => new
                {
                    Key = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessAuditEntries", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Groups",
                columns: table => new
                {
                    Key = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Groups", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "PermissionTargets",
                columns: table => new
                {
                    Key = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RepositoryPatterns = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    IncludePatterns = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ExcludePatterns = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ProjectKey = table.Column<int>(type: "integer", nullable: true),
                    IsBuiltIn = table.Column<bool>(type: "boolean", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermissionTargets", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ProjectMembers",
                columns: table => new
                {
                    Key = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectKey = table.Column<int>(type: "integer", nullable: false),
                    PrincipalType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PrincipalId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectMembers", x => x.Key);
                    table.ForeignKey(
                        name: "FK_ProjectMembers_Projects_ProjectKey",
                        column: x => x.ProjectKey,
                        principalTable: "Projects",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupMembers",
                columns: table => new
                {
                    Key = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GroupKey = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupMembers", x => x.Key);
                    table.ForeignKey(
                        name: "FK_GroupMembers_Groups_GroupKey",
                        column: x => x.GroupKey,
                        principalTable: "Groups",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PermissionGrants",
                columns: table => new
                {
                    Key = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TargetKey = table.Column<int>(type: "integer", nullable: false),
                    PrincipalType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PrincipalId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Actions = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermissionGrants", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PermissionGrants_PermissionTargets_TargetKey",
                        column: x => x.TargetKey,
                        principalTable: "PermissionTargets",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            // The upgrade defaults: what the server did before the access model existed. Everyone reads every
            // repository; every authenticated caller deploys, annotates, deletes and manages (package ownership is
            // still applied inside those actions). Explicit keys, so the grants below can name them.
            var seeded = DateTime.UtcNow;

            migrationBuilder.InsertData(
                table: "Groups",
                columns: new[] { "Key", "Name", "Description", "Created", "IsBuiltIn" },
                values: new object[,]
                {
                    { 1, "Everyone", "Every caller, anonymous ones included.", seeded, true },
                    { 2, "Authenticated", "Every caller that presented valid credentials.", seeded, true },
                });

            migrationBuilder.InsertData(
                table: "PermissionTargets",
                columns: new[] { "Key", "Name", "RepositoryPatterns", "IncludePatterns", "ExcludePatterns", "IsBuiltIn", "Created" },
                values: new object[,]
                {
                    { 1, "Anyone reads every repository", "[\"*\"]", "[\"*\"]", "[]", true, seeded },
                    { 2, "Authenticated users deploy", "[\"*\"]", "[\"*\"]", "[]", true, seeded },
                });

            // Actions: Read = 1; Annotate | Deploy | Delete | Manage = 2 + 4 + 8 + 16 = 30.
            migrationBuilder.InsertData(
                table: "PermissionGrants",
                columns: new[] { "Key", "TargetKey", "PrincipalType", "PrincipalId", "Actions" },
                values: new object[,]
                {
                    { 1, 1, "Group", "1", 1 },
                    { 2, 2, "Group", "2", 30 },
                });

            // Explicit keys do not advance an identity sequence: make the next generated key of each table 3.
            migrationBuilder.Sql("SELECT setval(pg_get_serial_sequence('\"Groups\"', 'Key'), 2);");
            migrationBuilder.Sql("SELECT setval(pg_get_serial_sequence('\"PermissionTargets\"', 'Key'), 2);");
            migrationBuilder.Sql("SELECT setval(pg_get_serial_sequence('\"PermissionGrants\"', 'Key'), 2);");

            migrationBuilder.CreateIndex(
                name: "IX_AccessAuditEntries_Created",
                table: "AccessAuditEntries",
                column: "Created");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_GroupKey_UserId",
                table: "GroupMembers",
                columns: new[] { "GroupKey", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_UserId",
                table: "GroupMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_Name",
                table: "Groups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PermissionGrants_TargetKey_PrincipalType_PrincipalId",
                table: "PermissionGrants",
                columns: new[] { "TargetKey", "PrincipalType", "PrincipalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PermissionTargets_Name",
                table: "PermissionTargets",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMembers_ProjectKey_PrincipalType_PrincipalId",
                table: "ProjectMembers",
                columns: new[] { "ProjectKey", "PrincipalType", "PrincipalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessAuditEntries");

            migrationBuilder.DropTable(
                name: "GroupMembers");

            migrationBuilder.DropTable(
                name: "PermissionGrants");

            migrationBuilder.DropTable(
                name: "ProjectMembers");

            migrationBuilder.DropTable(
                name: "Groups");

            migrationBuilder.DropTable(
                name: "PermissionTargets");
        }
    }
}
