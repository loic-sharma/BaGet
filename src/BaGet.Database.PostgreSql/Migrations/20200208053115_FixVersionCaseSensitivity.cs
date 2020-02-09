using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace BaGet.Database.PostgreSql.Migrations
{
    public partial class FixVersionCaseSensitivity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Make the "version" column case insensitive.
            migrationBuilder.AlterColumn<string>(
                name: "Version",
                table: "Packages",
                type: "citext",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldMaxLength: 64);

            // Npgsql 3.0 migration
            // See: http://www.npgsql.org/efcore/release-notes/3.0.html#default-value-generation-strategy-is-now-identity
            migrationBuilder.AlterColumn<int>(
                name: "Key",
                table: "TargetFrameworks",
                nullable: false,
                oldClrType: typeof(int))
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn);

            migrationBuilder.AlterColumn<int>(
                name: "Key",
                table: "PackageTypes",
                nullable: false,
                oldClrType: typeof(int))
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn);

            migrationBuilder.AlterColumn<int>(
                name: "Key",
                table: "Packages",
                nullable: false,
                oldClrType: typeof(int))
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn);

            migrationBuilder.AlterColumn<int>(
                name: "Key",
                table: "PackageDependencies",
                nullable: false,
                oldClrType: typeof(int))
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Version",
                table: "Packages",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "citext",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<int>(
                name: "Key",
                table: "TargetFrameworks",
                nullable: false,
                oldClrType: typeof(int))
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<int>(
                name: "Key",
                table: "PackageTypes",
                nullable: false,
                oldClrType: typeof(int))
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<int>(
                name: "Key",
                table: "Packages",
                nullable: false,
                oldClrType: typeof(int))
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<int>(
                name: "Key",
                table: "PackageDependencies",
                nullable: false,
                oldClrType: typeof(int))
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
        }
    }
}
