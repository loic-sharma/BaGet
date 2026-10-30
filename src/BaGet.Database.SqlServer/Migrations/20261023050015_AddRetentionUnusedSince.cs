using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGet.Database.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddRetentionUnusedSince : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UnusedSince",
                table: "RetentionPolicies",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnusedSince",
                table: "RetentionPolicies");
        }
    }
}
