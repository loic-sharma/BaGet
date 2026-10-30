using BaGet.Core;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BaGet.Database.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnershipForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rows that pointed at nothing are removed first (children before parents), or the constraints below could not be created.
            // On SQLite this runs before the table rebuilds that add the foreign keys.
            foreach (var sql in OwnershipOrphanCleanup.Statements(OwnershipEngine.PostgreSql))
            {
                migrationBuilder.Sql(sql);
            }

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_ProjectKey",
                table: "Repositories",
                column: "ProjectKey");

            migrationBuilder.CreateIndex(
                name: "IX_PermissionTargets_ProjectKey",
                table: "PermissionTargets",
                column: "ProjectKey");

            migrationBuilder.AddForeignKey(
                name: "FK_AbuseReports_Repositories_RepositoryKey",
                table: "AbuseReports",
                column: "RepositoryKey",
                principalTable: "Repositories",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ApiKeyRepositories_Repositories_RepositoryKey",
                table: "ApiKeyRepositories",
                column: "RepositoryKey",
                principalTable: "Repositories",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ApiKeys_AspNetUsers_UserId",
                table: "ApiKeys",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ArtifactUsages_Repositories_RepositoryKey",
                table: "ArtifactUsages",
                column: "RepositoryKey",
                principalTable: "Repositories",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CatalogLeafs_Repositories_RepositoryKey",
                table: "CatalogLeafs",
                column: "RepositoryKey",
                principalTable: "Repositories",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GroupMembers_AspNetUsers_UserId",
                table: "GroupMembers",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MavenArtifacts_Repositories_RepositoryKey",
                table: "MavenArtifacts",
                column: "RepositoryKey",
                principalTable: "Repositories",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_NpmPackages_Repositories_RepositoryKey",
                table: "NpmPackages",
                column: "RepositoryKey",
                principalTable: "Repositories",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OciBlobs_Repositories_RepositoryKey",
                table: "OciBlobs",
                column: "RepositoryKey",
                principalTable: "Repositories",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OciRepositories_Repositories_RepositoryKey",
                table: "OciRepositories",
                column: "RepositoryKey",
                principalTable: "Repositories",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OciTags_OciRepositories_ImageKey",
                table: "OciTags",
                column: "ImageKey",
                principalTable: "OciRepositories",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationMembers_AspNetUsers_UserId",
                table: "OrganizationMembers",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PackageOwners_Repositories_RepositoryKey",
                table: "PackageOwners",
                column: "RepositoryKey",
                principalTable: "Repositories",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Packages_Repositories_RepositoryKey",
                table: "Packages",
                column: "RepositoryKey",
                principalTable: "Repositories",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PackageSymbols_Repositories_RepositoryKey",
                table: "PackageSymbols",
                column: "RepositoryKey",
                principalTable: "Repositories",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PackageValidations_Repositories_RepositoryKey",
                table: "PackageValidations",
                column: "RepositoryKey",
                principalTable: "Repositories",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PermissionTargets_Projects_ProjectKey",
                table: "PermissionTargets",
                column: "ProjectKey",
                principalTable: "Projects",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReplicationConfigs_Repositories_RepositoryKey",
                table: "ReplicationConfigs",
                column: "RepositoryKey",
                principalTable: "Repositories",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReplicationItems_ReplicationConfigs_ConfigKey",
                table: "ReplicationItems",
                column: "ConfigKey",
                principalTable: "ReplicationConfigs",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReplicationRuns_ReplicationConfigs_ConfigKey",
                table: "ReplicationRuns",
                column: "ConfigKey",
                principalTable: "ReplicationConfigs",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Repositories_Projects_ProjectKey",
                table: "Repositories",
                column: "ProjectKey",
                principalTable: "Projects",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RetentionHolds_Repositories_RepositoryKey",
                table: "RetentionHolds",
                column: "RepositoryKey",
                principalTable: "Repositories",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RetentionPolicies_Projects_ProjectKey",
                table: "RetentionPolicies",
                column: "ProjectKey",
                principalTable: "Projects",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RetentionPolicies_Repositories_RepositoryKey",
                table: "RetentionPolicies",
                column: "RepositoryKey",
                principalTable: "Repositories",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ScanPolicies_Projects_ProjectKey",
                table: "ScanPolicies",
                column: "ProjectKey",
                principalTable: "Projects",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ScanPolicies_Repositories_RepositoryKey",
                table: "ScanPolicies",
                column: "RepositoryKey",
                principalTable: "Repositories",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ScanResults_Repositories_RepositoryKey",
                table: "ScanResults",
                column: "RepositoryKey",
                principalTable: "Repositories",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AbuseReports_Repositories_RepositoryKey",
                table: "AbuseReports");

            migrationBuilder.DropForeignKey(
                name: "FK_ApiKeyRepositories_Repositories_RepositoryKey",
                table: "ApiKeyRepositories");

            migrationBuilder.DropForeignKey(
                name: "FK_ApiKeys_AspNetUsers_UserId",
                table: "ApiKeys");

            migrationBuilder.DropForeignKey(
                name: "FK_ArtifactUsages_Repositories_RepositoryKey",
                table: "ArtifactUsages");

            migrationBuilder.DropForeignKey(
                name: "FK_CatalogLeafs_Repositories_RepositoryKey",
                table: "CatalogLeafs");

            migrationBuilder.DropForeignKey(
                name: "FK_GroupMembers_AspNetUsers_UserId",
                table: "GroupMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_MavenArtifacts_Repositories_RepositoryKey",
                table: "MavenArtifacts");

            migrationBuilder.DropForeignKey(
                name: "FK_NpmPackages_Repositories_RepositoryKey",
                table: "NpmPackages");

            migrationBuilder.DropForeignKey(
                name: "FK_OciBlobs_Repositories_RepositoryKey",
                table: "OciBlobs");

            migrationBuilder.DropForeignKey(
                name: "FK_OciRepositories_Repositories_RepositoryKey",
                table: "OciRepositories");

            migrationBuilder.DropForeignKey(
                name: "FK_OciTags_OciRepositories_ImageKey",
                table: "OciTags");

            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationMembers_AspNetUsers_UserId",
                table: "OrganizationMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_PackageOwners_Repositories_RepositoryKey",
                table: "PackageOwners");

            migrationBuilder.DropForeignKey(
                name: "FK_Packages_Repositories_RepositoryKey",
                table: "Packages");

            migrationBuilder.DropForeignKey(
                name: "FK_PackageSymbols_Repositories_RepositoryKey",
                table: "PackageSymbols");

            migrationBuilder.DropForeignKey(
                name: "FK_PackageValidations_Repositories_RepositoryKey",
                table: "PackageValidations");

            migrationBuilder.DropForeignKey(
                name: "FK_PermissionTargets_Projects_ProjectKey",
                table: "PermissionTargets");

            migrationBuilder.DropForeignKey(
                name: "FK_ReplicationConfigs_Repositories_RepositoryKey",
                table: "ReplicationConfigs");

            migrationBuilder.DropForeignKey(
                name: "FK_ReplicationItems_ReplicationConfigs_ConfigKey",
                table: "ReplicationItems");

            migrationBuilder.DropForeignKey(
                name: "FK_ReplicationRuns_ReplicationConfigs_ConfigKey",
                table: "ReplicationRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_Repositories_Projects_ProjectKey",
                table: "Repositories");

            migrationBuilder.DropForeignKey(
                name: "FK_RetentionHolds_Repositories_RepositoryKey",
                table: "RetentionHolds");

            migrationBuilder.DropForeignKey(
                name: "FK_RetentionPolicies_Projects_ProjectKey",
                table: "RetentionPolicies");

            migrationBuilder.DropForeignKey(
                name: "FK_RetentionPolicies_Repositories_RepositoryKey",
                table: "RetentionPolicies");

            migrationBuilder.DropForeignKey(
                name: "FK_ScanPolicies_Projects_ProjectKey",
                table: "ScanPolicies");

            migrationBuilder.DropForeignKey(
                name: "FK_ScanPolicies_Repositories_RepositoryKey",
                table: "ScanPolicies");

            migrationBuilder.DropForeignKey(
                name: "FK_ScanResults_Repositories_RepositoryKey",
                table: "ScanResults");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_ProjectKey",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_PermissionTargets_ProjectKey",
                table: "PermissionTargets");
        }
    }
}
