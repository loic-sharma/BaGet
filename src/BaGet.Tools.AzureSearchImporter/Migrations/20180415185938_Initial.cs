using Microsoft.EntityFrameworkCore.Migrations;
using System;
using System.Collections.Generic;

namespace BaGet.Tools.AzureSearchImporter.Migrations
{
    public partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PackageIds",
                columns: table => new
                {
                    Key = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Done = table.Column<bool>(nullable: false),
                    Value = table.Column<string>(type: "TEXT COLLATE NOCASE", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageIds", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackageIds_Done",
                table: "PackageIds",
                column: "Done");

            migrationBuilder.CreateIndex(
                name: "IX_PackageIds_Value",
                table: "PackageIds",
                column: "Value",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackageIds");
        }
    }
}
