using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGet.Database.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageSymbols : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PackageSymbols",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RepositoryKey = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    PackageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PackageVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SymbolKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageSymbols", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackageSymbols_RepositoryKey_FileName_SymbolKey",
                table: "PackageSymbols",
                columns: new[] { "RepositoryKey", "FileName", "SymbolKey" });

            migrationBuilder.CreateIndex(
                name: "IX_PackageSymbols_RepositoryKey_PackageId_PackageVersion",
                table: "PackageSymbols",
                columns: new[] { "RepositoryKey", "PackageId", "PackageVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackageSymbols");
        }
    }
}
