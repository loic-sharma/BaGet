using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BaGet.Core;

public interface IContext
{
    DatabaseFacade Database { get; }

    DbSet<Package>? Packages { get; set; }

    /// <summary>
    /// The dependencies declared by packages. A dependency belongs to the package that
    /// declares it, but the relationship is optional in the database, so deleting a
    /// package does not delete them.
    /// </summary>
    DbSet<PackageDependency>? PackageDependencies { get; set; }

    /// <summary>
    /// The deprecations of package versions.
    /// </summary>
    DbSet<PackageDeprecation>? PackageDeprecations { get; set; }

    /// <summary>
    /// The known vulnerabilities affecting package versions.
    /// </summary>
    DbSet<PackageVulnerability>? PackageVulnerabilities { get; set; }

    DbSet<CatalogEntry>? CatalogLeafs { get; set; }

    DbSet<Organization>? Organizations { get; set; }

    DbSet<OrganizationMember>? OrganizationMembers { get; set; }

    DbSet<PackageOwner>? PackageOwners { get; set; }

    DbSet<ApiKey>? ApiKeys { get; set; }

    /// <summary>The repositories an API key is limited to; see <see cref="ApiKeyRepository"/>.</summary>
    DbSet<ApiKeyRepository>? ApiKeyRepositories { get; set; }

    DbSet<PackageValidation>? PackageValidations { get; set; }

    DbSet<AbuseReport>? AbuseReports { get; set; }

    /// <summary>
    /// Which package version each indexed PDB came from; see <see cref="PackageSymbol"/>.
    /// </summary>
    DbSet<PackageSymbol>? PackageSymbols { get; set; }

    /// <summary>What vulnerability scans found per artifact version; see <see cref="ScanResult"/>. Repository-scoped.</summary>
    DbSet<ScanResult>? ScanResults { get; set; }

    /// <summary>The findings of a <see cref="ScanResult"/>.</summary>
    DbSet<ScanFinding>? ScanFindings { get; set; }

    /// <summary>The scanning policies of repositories and projects; see <see cref="ScanPolicy"/>.</summary>
    DbSet<ScanPolicy>? ScanPolicies { get; set; }

    /// <summary>npm packages (packuments); see <see cref="NpmPackage"/>. Repository-scoped.</summary>
    DbSet<NpmPackage>? NpmPackages { get; set; }

    /// <summary>The published versions of npm packages; see <see cref="NpmVersion"/>.</summary>
    DbSet<NpmVersion>? NpmVersions { get; set; }

    /// <summary>The dist-tags of npm packages; see <see cref="NpmDistTag"/>.</summary>
    DbSet<NpmDistTag>? NpmDistTags { get; set; }

    /// <summary>Maven artifacts (group and artifact id); see <see cref="MavenArtifact"/>. Repository-scoped.</summary>
    DbSet<MavenArtifact>? MavenArtifacts { get; set; }

    /// <summary>The versions of Maven artifacts; see <see cref="MavenVersion"/>.</summary>
    DbSet<MavenVersion>? MavenVersions { get; set; }

    /// <summary>The files of Maven versions, with their checksums; see <see cref="MavenFile"/>.</summary>
    DbSet<MavenFile>? MavenFiles { get; set; }

    /// <summary>The image repositories of Docker repositories; see <see cref="OciRepository"/>. Repository-scoped.</summary>
    DbSet<OciRepository>? OciRepositories { get; set; }

    /// <summary>The manifests and indexes of images; see <see cref="OciManifest"/>.</summary>
    DbSet<OciManifest>? OciManifests { get; set; }

    /// <summary>The tags of images; see <see cref="OciTag"/>.</summary>
    DbSet<OciTag>? OciTags { get; set; }

    /// <summary>The blobs of a Docker repository, by digest; see <see cref="OciBlob"/>. Repository-scoped.</summary>
    DbSet<OciBlob>? OciBlobs { get; set; }

    /// <summary>Which images hold which blobs; see <see cref="OciBlobLink"/>.</summary>
    DbSet<OciBlobLink>? OciBlobLinks { get; set; }

    /// <summary>The open blob upload sessions; see <see cref="OciUpload"/>.</summary>
    DbSet<OciUpload>? OciUploads { get; set; }

    /// <summary>
    /// The repositories. Not repository-scoped: it lists them all.
    /// </summary>
    DbSet<Repository>? Repositories { get; set; }

    DbSet<Project>? Projects { get; set; }

    /// <summary>The access model: groups, their members, permission targets and grants, project members and the audit log.</summary>
    DbSet<Group>? Groups { get; set; }

    DbSet<GroupMember>? GroupMembers { get; set; }

    DbSet<PermissionTarget>? PermissionTargets { get; set; }

    DbSet<PermissionGrant>? PermissionGrants { get; set; }

    DbSet<ProjectMember>? ProjectMembers { get; set; }

    DbSet<AccessAuditEntry>? AccessAuditEntries { get; set; }

    /// <summary>The retention policies; see <see cref="RetentionPolicy"/>. Not repository-scoped.</summary>
    DbSet<RetentionPolicy>? RetentionPolicies { get; set; }

    /// <summary>The legal holds; see <see cref="RetentionHold"/>. Repository-scoped.</summary>
    DbSet<RetentionHold>? RetentionHolds { get; set; }

    /// <summary>When versions were last downloaded; see <see cref="ArtifactUsage"/>. Repository-scoped.</summary>
    DbSet<ArtifactUsage>? ArtifactUsages { get; set; }

    /// <summary>The replication configurations; see <see cref="ReplicationConfig"/>. Not repository-scoped.</summary>
    DbSet<ReplicationConfig>? ReplicationConfigs { get; set; }

    /// <summary>The replication log; see <see cref="ReplicationRun"/>. Not repository-scoped.</summary>
    DbSet<ReplicationRun>? ReplicationRuns { get; set; }

    /// <summary>What replication copied; see <see cref="ReplicationItem"/>. Not repository-scoped.</summary>
    DbSet<ReplicationItem>? ReplicationItems { get; set; }

    /// <summary>The ASP.NET Core data protection key ring when <c>Ha:Enabled</c>; see <see cref="DataProtectionKeyEntry"/>. Not repository-scoped.</summary>
    DbSet<DataProtectionKeyEntry>? DataProtectionKeys { get; set; }

    /// <summary>
    /// Identity's users. Declared here so that account services can query them through
    /// the same context as everything else.
    /// </summary>
    DbSet<BaGetUser> Users { get; set; }

    /// <summary>
    /// Check whether a <see cref="DbUpdateException"/> is due to a SQL unique constraint violation.
    /// </summary>
    /// <param name="exception">The exception to inspect.</param>
    /// <returns>Whether the exception was caused to SQL unique constraint violation.</returns>
    bool IsUniqueConstraintViolationException(DbUpdateException exception);

    /// <summary>
    /// Whether this database engine supports LINQ "Take" in subqueries.
    /// </summary>
    bool SupportsLimitInSubqueries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Applies any pending migrations for the context to the database.
    /// Creates the database if it does not already exist.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>A task that completes once migrations are applied.</returns>
    Task RunMigrationsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Prepares the database for inserting rows of <paramref name="table"/> with explicit values for
    /// their generated (identity/serial/auto-increment) key, as a data copy does. Call it before the
    /// inserts and <see cref="EndKeyedInsertAsync"/> after them, for one table at a time.
    /// </summary>
    /// <remarks>
    /// Providers that need it (SQL Server) hold the context's connection open between the two calls,
    /// because the setting is per connection; the caller must therefore call both, on the same context.
    /// Providers whose engine needs nothing (SQLite, MySQL) do nothing.
    /// </remarks>
    /// <param name="table">The EF entity type about to receive explicit keys.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task BeginKeyedInsertAsync(IEntityType table, CancellationToken cancellationToken);

    /// <summary>
    /// Completes the explicit-key inserts started by <see cref="BeginKeyedInsertAsync"/>, so that keys
    /// generated afterwards continue after the highest copied key (PostgreSQL resets its sequence here).
    /// </summary>
    /// <param name="table">The EF entity type whose rows were inserted.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task EndKeyedInsertAsync(IEntityType table, CancellationToken cancellationToken);
}
