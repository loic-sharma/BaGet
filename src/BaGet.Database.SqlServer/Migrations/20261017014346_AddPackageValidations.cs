using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGet.Database.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageValidations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PackageValidations",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubmissionId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    PackageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PackageVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PackageKey = table.Column<int>(type: "int", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Completed = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageValidations", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PackageValidations_Packages_PackageKey",
                        column: x => x.PackageKey,
                        principalTable: "Packages",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackageValidations_PackageId",
                table: "PackageValidations",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageValidations_PackageKey",
                table: "PackageValidations",
                column: "PackageKey");

            migrationBuilder.CreateIndex(
                name: "IX_PackageValidations_SubmissionId",
                table: "PackageValidations",
                column: "SubmissionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackageValidations");
        }
    }
}
