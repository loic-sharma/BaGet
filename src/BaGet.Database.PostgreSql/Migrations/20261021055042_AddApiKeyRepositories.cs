using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BaGet.Database.PostgreSql.Migrations
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
                    Key = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApiKeyKey = table.Column<int>(type: "integer", nullable: false),
                    RepositoryKey = table.Column<int>(type: "integer", nullable: false)
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
                });

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
