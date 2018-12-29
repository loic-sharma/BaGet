using Microsoft.EntityFrameworkCore.Migrations;

namespace BaGet.Migrations.Sqlite
{
    public partial class AddSourceCode : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SourceCodeAssemblies",
                columns: table => new
                {
                    Key = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Display = table.Column<string>(nullable: true),
                    Framework = table.Column<string>(nullable: true),
                    PackageKey = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceCodeAssemblies", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "SourceCodeTypes",
                columns: table => new
                {
                    Key = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FullName = table.Column<string>(nullable: true),
                    Display = table.Column<string>(nullable: true),
                    CSharp = table.Column<string>(nullable: true),
                    AssemblyKey = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceCodeTypes", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "SourceCodeMembers",
                columns: table => new
                {
                    Key = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MemberKind = table.Column<int>(nullable: false),
                    Name = table.Column<string>(nullable: true),
                    MemberType = table.Column<string>(nullable: true),
                    Display = table.Column<string>(nullable: true),
                    TypeKey = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceCodeMembers", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceCodeAssemblies_PackageKey",
                table: "SourceCodeAssemblies",
                column: "PackageKey");

            migrationBuilder.CreateIndex(
                name: "IX_SourceCodeMembers_TypeKey",
                table: "SourceCodeMembers",
                column: "TypeKey");

            migrationBuilder.CreateIndex(
                name: "IX_SourceCodeTypes_AssemblyKey",
                table: "SourceCodeTypes",
                column: "AssemblyKey");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceCodeMembers");

            migrationBuilder.DropTable(
                name: "SourceCodeTypes");

            migrationBuilder.DropTable(
                name: "SourceCodeAssemblies");

            migrationBuilder.AlterColumn<int>(
                name: "PackageKey",
                table: "PackageDependencies",
                nullable: true,
                oldClrType: typeof(int));
        }
    }
}
