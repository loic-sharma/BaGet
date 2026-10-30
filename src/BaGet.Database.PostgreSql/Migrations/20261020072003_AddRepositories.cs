using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BaGet.Database.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddRepositories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PackageValidations_PackageId",
                table: "PackageValidations");

            migrationBuilder.DropIndex(
                name: "IX_Packages_Id_Version",
                table: "Packages");

            migrationBuilder.DropIndex(
                name: "IX_PackageOwners_PackageId",
                table: "PackageOwners");

            migrationBuilder.DropIndex(
                name: "IX_AbuseReports_PackageId",
                table: "AbuseReports");

            migrationBuilder.AddColumn<int>(
                name: "RepositoryKey",
                table: "PackageValidations",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "RepositoryKey",
                table: "Packages",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "RepositoryKey",
                table: "PackageOwners",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "RepositoryKey",
                table: "CatalogLeafs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "RepositoryKey",
                table: "AbuseReports",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Key = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Repositories",
                columns: table => new
                {
                    Key = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProjectKey = table.Column<int>(type: "integer", nullable: true),
                    Settings = table.Column<string>(type: "text", nullable: true),
                    AnonymousRead = table.Column<bool>(type: "boolean", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repositories", x => x.Key);
                });

            // The default repository (key 1 = RepositoryKey's column default, so every existing row already belongs to
            // it). Created here, not only by startup, so that applying migrations without RunMigrationsAtStartup
            // (dotnet ef database update, an SQL script) cannot leave a new repository to take key 1.
            migrationBuilder.InsertData(
                table: "Repositories",
                columns: new[] { "Key", "Name", "Format", "Kind", "AnonymousRead", "Created" },
                values: new object[] { 1, "default", "NuGet", "Local", true, DateTime.UtcNow });

            // An explicit key does not advance an identity sequence: make the next generated key 2.
            migrationBuilder.Sql("SELECT setval(pg_get_serial_sequence('\"Repositories\"', 'Key'), 1);");

            migrationBuilder.CreateIndex(
                name: "IX_PackageValidations_RepositoryKey_PackageId",
                table: "PackageValidations",
                columns: new[] { "RepositoryKey", "PackageId" });

            migrationBuilder.CreateIndex(
                name: "IX_Packages_RepositoryKey_Id_Version",
                table: "Packages",
                columns: new[] { "RepositoryKey", "Id", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackageOwners_RepositoryKey_PackageId",
                table: "PackageOwners",
                columns: new[] { "RepositoryKey", "PackageId" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogLeafs_RepositoryKey",
                table: "CatalogLeafs",
                column: "RepositoryKey");

            migrationBuilder.CreateIndex(
                name: "IX_AbuseReports_RepositoryKey_PackageId",
                table: "AbuseReports",
                columns: new[] { "RepositoryKey", "PackageId" });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_Name",
                table: "Repositories",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_PackageValidations_RepositoryKey_PackageId",
                table: "PackageValidations");

            migrationBuilder.DropIndex(
                name: "IX_Packages_RepositoryKey_Id_Version",
                table: "Packages");

            migrationBuilder.DropIndex(
                name: "IX_PackageOwners_RepositoryKey_PackageId",
                table: "PackageOwners");

            migrationBuilder.DropIndex(
                name: "IX_CatalogLeafs_RepositoryKey",
                table: "CatalogLeafs");

            migrationBuilder.DropIndex(
                name: "IX_AbuseReports_RepositoryKey_PackageId",
                table: "AbuseReports");

            migrationBuilder.DropColumn(
                name: "RepositoryKey",
                table: "PackageValidations");

            migrationBuilder.DropColumn(
                name: "RepositoryKey",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "RepositoryKey",
                table: "PackageOwners");

            migrationBuilder.DropColumn(
                name: "RepositoryKey",
                table: "CatalogLeafs");

            migrationBuilder.DropColumn(
                name: "RepositoryKey",
                table: "AbuseReports");

            migrationBuilder.CreateIndex(
                name: "IX_PackageValidations_PackageId",
                table: "PackageValidations",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_Packages_Id_Version",
                table: "Packages",
                columns: new[] { "Id", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackageOwners_PackageId",
                table: "PackageOwners",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_AbuseReports_PackageId",
                table: "AbuseReports",
                column: "PackageId");
        }
    }
}
