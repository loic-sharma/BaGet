using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BaGet.Database.SqlServer.Migrations
{
    public partial class AddSearchDimensions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPrerelease",
                table: "Packages",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SemVerLevel",
                table: "Packages",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PackageTypes",
                columns: table => new
                {
                    Key = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(maxLength: 512, nullable: true),
                    Version = table.Column<string>(maxLength: 64, nullable: true),
                    PackageKey = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageTypes", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PackageTypes_Packages_PackageKey",
                        column: x => x.PackageKey,
                        principalTable: "Packages",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TargetFrameworks",
                columns: table => new
                {
                    Key = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Moniker = table.Column<string>(maxLength: 256, nullable: true),
                    PackageKey = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TargetFrameworks", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TargetFrameworks_Packages_PackageKey",
                        column: x => x.PackageKey,
                        principalTable: "Packages",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackageDependencies_Id",
                table: "PackageDependencies",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_PackageTypes_Name",
                table: "PackageTypes",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_PackageTypes_PackageKey",
                table: "PackageTypes",
                column: "PackageKey");

            migrationBuilder.CreateIndex(
                name: "IX_TargetFrameworks_Moniker",
                table: "TargetFrameworks",
                column: "Moniker");

            migrationBuilder.CreateIndex(
                name: "IX_TargetFrameworks_PackageKey",
                table: "TargetFrameworks",
                column: "PackageKey");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackageTypes");

            migrationBuilder.DropTable(
                name: "TargetFrameworks");

            migrationBuilder.DropIndex(
                name: "IX_PackageDependencies_Id",
                table: "PackageDependencies");

            migrationBuilder.DropColumn(
                name: "IsPrerelease",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "SemVerLevel",
                table: "Packages");
        }
    }
}
