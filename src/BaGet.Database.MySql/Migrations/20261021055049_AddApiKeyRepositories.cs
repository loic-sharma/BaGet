using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGet.Database.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyRepositories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiKeyRepositories",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ApiKeyKey = table.Column<int>(type: "int", nullable: false),
                    RepositoryKey = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeyRepositories", x => x.Key);
                    table.ForeignKey(
                        name: "FK_ApiKeyRepositories_ApiKeys_ApiKeyKey",
                        column: x => x.ApiKeyKey,
                        principalTable: "ApiKeys",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeyRepositories_ApiKeyKey_RepositoryKey",
                table: "ApiKeyRepositories",
                columns: new[] { "ApiKeyKey", "RepositoryKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeyRepositories_RepositoryKey",
                table: "ApiKeyRepositories",
                column: "RepositoryKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeyRepositories");
        }
    }
}
