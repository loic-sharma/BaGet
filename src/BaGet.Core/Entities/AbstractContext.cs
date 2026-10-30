using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BaGet.Core;

/// <remarks>
/// Identity's tables live in the same database as the packages, so a deployment keeps
/// one database. They are created by a migration whether or not accounts are enabled.
/// </remarks>
public abstract class AbstractContext<TContext> : IdentityDbContext<BaGetUser>, IContext, IRepositoryScopedContext where TContext : DbContext
{
    public const int DefaultMaxStringLength = 4000;

    public const int MaxPackageIdLength = 128;
    public const int MaxPackageVersionLength = 64;
    public const int MaxPackageMinClientVersionLength = 44;
    public const int MaxPackageLanguageLength = 20;
    public const int MaxPackageTitleLength = 256;
    public const int MaxPackageTypeNameLength = 512;
    public const int MaxPackageTypeVersionLength = 64;
    public const int MaxRepositoryTypeLength = 100;
    public const int MaxTargetFrameworkLength = 256;

    public const int MaxPackageDependencyVersionRangeLength = 256;

    public const int MaxCatalogCommitIdLength = 36;
    public const int MaxCatalogTypeLength = 32;

    public const int MaxAccountNameLength = 256;
    public const int MaxUserIdLength = 450;
    public const int MaxApiKeyDescriptionLength = 256;
    public const int MaxApiKeyHashLength = 64;

    public const int MaxRepositoryNameLength = RepositoryNames.MaxLength;
    public const int MaxRepositoryFormatLength = 32;

    // npm's own limits: a package name (scope included) is at most 214 characters, a version and a dist-tag fit 256.
    public const int MaxScanNameLength = ScanLimits.NameLength;
    public const int MaxScanVersionLength = ScanLimits.VersionLength;
    public const int MaxScanIdLength = ScanLimits.IdLength;
    public const int MaxScanSummaryLength = ScanLimits.SummaryLength;
    public const int MaxScanUrlLength = ScanLimits.UrlLength;
    public const int MaxScanErrorLength = ScanLimits.ErrorLength;
    public const int MaxScanScannerLength = ScanLimits.ScannerLength;
    public const int MaxNpmNameLength = 214;
    public const int MaxNpmVersionLength = 256;
    public const int MaxNpmTagLength = 128;
    public const int MaxNpmIntegrityLength = 256;
    public const int MaxNpmShasumLength = 40;
    // Maven's layout is folders, so the lengths keep the unique indexes (group, artifact; version, file) within SQL Server's and
    // MySQL's key limits.
    public const int MaxMavenGroupLength = 200;
    public const int MaxMavenArtifactLength = 128;
    public const int MaxMavenVersionLength = 128;
    public const int MaxMavenFileNameLength = 256;
    public const int MaxMavenClassifierLength = 64;
    public const int MaxMavenExtensionLength = 64;
    public const int MaxMavenNameLength = 512;
    public const int MaxMavenDescriptionLength = 4000;
    public const int MaxMavenPackagingLength = 64;
    public const int MaxMavenSnapshotTimestampLength = 15;
    public const int MaxMavenChecksumLength = 128;
    // OCI: image names and tags are bounded by the specification (a tag by 128), digests by "sha256:" + 64 hex; the unique indexes
    // (repository, image), (image, tag) stay within the key limits of SQL Server and MySQL.
    public const int MaxOciImageLength = OciNames.MaxImageLength;
    public const int MaxOciTagLength = OciNames.MaxTagLength;
    public const int MaxOciDigestLength = OciNames.MaxDigestLength;
    public const int MaxOciMediaTypeLength = OciNames.MaxMediaTypeLength;
    public const int MaxOciUploadReferenceLength = 128;
    public const int MaxOciStorageUploadIdLength = 512;
    // Retention: the longest Maven group:artifact (200 + 1 + 128) fits; with the version the unique indexes stay within SQL Server's and MySQL's key limits.
    public const int MaxRetentionNameLength = RetentionLimits.MaxNameLength;
    public const int MaxRetentionVersionLength = RetentionLimits.MaxVersionLength;
    public const int MaxRetentionReasonLength = RetentionLimits.MaxReasonLength;
    public const int MaxRetentionPatternsLength = RetentionLimits.MaxPatternsLength;
    public const int MaxRetentionResultLength = RetentionLimits.MaxResultLength;
    public const int MaxProjectNameLength = 64;
    public const int MaxProjectDisplayNameLength = 256;

    public const int MaxAccessNameLength = AccessNames.MaxLength;
    public const int MaxAccessDescriptionLength = 256;
    public const int MaxPrincipalTypeLength = 16;
    public const int MaxAuditActionLength = 64;

    public const int MaxValidationTypeLength = 64;

    public const int MaxAbuseReasonLength = 128;
    public const int MaxPdbFileNameLength = 255;
    public const int MaxSymbolKeyLength = 64;
    public const int MaxEmailLength = 256;

    private IRepositoryContext? _repository;

    public AbstractContext(DbContextOptions<TContext> options)
        : base(options)
    { }

    /// <summary>
    /// The repository this context reads and writes: every <see cref="IRepositoryScoped"/> query is filtered on it
    /// and new rows are stamped with it. Resolved from the application's DI scope on first use when the context
    /// was created by DI; a context built by hand (a database copy's target, design time) works in the default
    /// repository unless this is set. Not a security boundary on its own: <c>IgnoreQueryFilters()</c> sees all.
    /// </summary>
    public IRepositoryContext RepositoryScope
    {
        get => _repository ??= ResolveRepositoryScope() ?? DefaultRepositoryContext.Instance;
        set => _repository = value ?? throw new ArgumentNullException(nameof(value));
    }

    // AddDbContext hands the context the application's provider (the request scope); a hand-built context has none.
    private IRepositoryContext? ResolveRepositoryScope()
        => this.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()
            ?.ApplicationServiceProvider?.GetService(typeof(IRepositoryContext)) as IRepositoryContext;

    /// <summary>
    /// The key the query filters compare against. Evaluated per query, so it follows
    /// <see cref="IRepositoryContext.Use"/> called after the context was created.
    /// </summary>
    protected int CurrentRepositoryKey => RepositoryScope.Current.Key;

    /// <summary>The repository this context works in now (what <see cref="ReplicationDeployInterceptor"/> reports a deploy for).</summary>
    public int ScopeRepositoryKey => CurrentRepositoryKey;

    public DbSet<Package>? Packages { get; set; }
    public DbSet<PackageDependency>? PackageDependencies { get; set; }
    public DbSet<PackageType>? PackageTypes { get; set; }
    public DbSet<TargetFramework>? TargetFrameworks { get; set; }
    public DbSet<PackageDeprecation>? PackageDeprecations { get; set; }
    public DbSet<PackageVulnerability>? PackageVulnerabilities { get; set; }
    public DbSet<CatalogEntry>? CatalogLeafs { get; set; }
    public DbSet<Organization>? Organizations { get; set; }
    public DbSet<OrganizationMember>? OrganizationMembers { get; set; }
    public DbSet<PackageOwner>? PackageOwners { get; set; }
    public DbSet<ApiKey>? ApiKeys { get; set; }
    public DbSet<ApiKeyRepository>? ApiKeyRepositories { get; set; }
    public DbSet<PackageValidation>? PackageValidations { get; set; }
    public DbSet<AbuseReport>? AbuseReports { get; set; }
    public DbSet<PackageSymbol>? PackageSymbols { get; set; }
    public DbSet<ScanResult>? ScanResults { get; set; }
    public DbSet<ScanFinding>? ScanFindings { get; set; }
    public DbSet<ScanPolicy>? ScanPolicies { get; set; }
    public DbSet<NpmPackage>? NpmPackages { get; set; }
    public DbSet<NpmVersion>? NpmVersions { get; set; }
    public DbSet<NpmDistTag>? NpmDistTags { get; set; }
    public DbSet<MavenArtifact>? MavenArtifacts { get; set; }
    public DbSet<MavenVersion>? MavenVersions { get; set; }
    public DbSet<MavenFile>? MavenFiles { get; set; }
    public DbSet<OciRepository>? OciRepositories { get; set; }
    public DbSet<OciManifest>? OciManifests { get; set; }
    public DbSet<OciTag>? OciTags { get; set; }
    public DbSet<OciBlob>? OciBlobs { get; set; }
    public DbSet<OciBlobLink>? OciBlobLinks { get; set; }
    public DbSet<OciUpload>? OciUploads { get; set; }
    public DbSet<Repository>? Repositories { get; set; }
    public DbSet<Project>? Projects { get; set; }
    public DbSet<Group>? Groups { get; set; }
    public DbSet<GroupMember>? GroupMembers { get; set; }
    public DbSet<PermissionTarget>? PermissionTargets { get; set; }
    public DbSet<PermissionGrant>? PermissionGrants { get; set; }
    public DbSet<ProjectMember>? ProjectMembers { get; set; }
    public DbSet<AccessAuditEntry>? AccessAuditEntries { get; set; }
    public DbSet<RetentionPolicy>? RetentionPolicies { get; set; }
    public DbSet<RetentionHold>? RetentionHolds { get; set; }
    public DbSet<ArtifactUsage>? ArtifactUsages { get; set; }
    public DbSet<ReplicationConfig>? ReplicationConfigs { get; set; }
    public DbSet<ReplicationRun>? ReplicationRuns { get; set; }
    public DbSet<ReplicationItem>? ReplicationItems { get; set; }
    public DbSet<DataProtectionKeyEntry>? DataProtectionKeys { get; set; }

    public Task<int> SaveChangesAsync() => SaveChangesAsync(default);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampRepository();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampRepository();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // A new row without a repository belongs to the current one. A row that already names one (a database copy,
    // an explicit move) is left alone.
    private void StampRepository()
    {
        foreach (var entry in ChangeTracker.Entries<IRepositoryScoped>().Where(e => e.State == EntityState.Added))
        {
            if (entry.Entity.RepositoryKey == 0) entry.Entity.RepositoryKey = CurrentRepositoryKey;
        }
    }

    public virtual async Task RunMigrationsAsync(CancellationToken cancellationToken)
        => await Database.MigrateAsync(cancellationToken);

    /// <inheritdoc/>
    /// <remarks>Does nothing by default: SQLite and MySQL advance their auto-increment counter from explicit keys.</remarks>
    public virtual Task BeginKeyedInsertAsync(IEntityType table, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc/>
    /// <remarks>Does nothing by default: SQLite and MySQL advance their auto-increment counter from explicit keys.</remarks>
    public virtual Task EndKeyedInsertAsync(IEntityType table, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public abstract bool IsUniqueConstraintViolationException(DbUpdateException exception);

    public virtual bool SupportsLimitInSubqueries => true;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identity's tables lose the AspNet prefix (migration RenameAspNetTables).
        builder.Entity<BaGetUser>().ToTable("Users");
        builder.Entity<IdentityRole>().ToTable("Roles");
        builder.Entity<IdentityUserRole<string>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<string>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("UserLogins");
        builder.Entity<IdentityUserToken<string>>().ToTable("UserTokens");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("RoleClaims");

        builder.Entity<Package>(BuildPackageEntity);
        builder.Entity<PackageDependency>(BuildPackageDependencyEntity);
        builder.Entity<PackageType>(BuildPackageTypeEntity);
        builder.Entity<TargetFramework>(BuildTargetFrameworkEntity);
        builder.Entity<PackageDeprecation>(BuildPackageDeprecationEntity);
        builder.Entity<PackageVulnerability>(BuildPackageVulnerabilityEntity);
        builder.Entity<CatalogEntry>(BuildCatalogEntryEntity);
        builder.Entity<Organization>(BuildOrganizationEntity);
        builder.Entity<OrganizationMember>(BuildOrganizationMemberEntity);
        builder.Entity<PackageOwner>(BuildPackageOwnerEntity);
        builder.Entity<ApiKey>(BuildApiKeyEntity);
        builder.Entity<ApiKeyRepository>(BuildApiKeyRepositoryEntity);
        builder.Entity<PackageValidation>(BuildPackageValidationEntity);
        builder.Entity<AbuseReport>(BuildAbuseReportEntity);
        builder.Entity<PackageSymbol>(BuildPackageSymbolEntity);
        builder.Entity<ScanResult>(BuildScanResultEntity);
        builder.Entity<ScanFinding>(BuildScanFindingEntity);
        builder.Entity<ScanPolicy>(BuildScanPolicyEntity);
        builder.Entity<NpmPackage>(BuildNpmPackageEntity);
        builder.Entity<NpmVersion>(BuildNpmVersionEntity);
        builder.Entity<NpmDistTag>(BuildNpmDistTagEntity);
        builder.Entity<MavenArtifact>(BuildMavenArtifactEntity);
        builder.Entity<MavenVersion>(BuildMavenVersionEntity);
        builder.Entity<MavenFile>(BuildMavenFileEntity);
        builder.Entity<OciRepository>(BuildOciRepositoryEntity);
        builder.Entity<OciManifest>(BuildOciManifestEntity);
        builder.Entity<OciTag>(BuildOciTagEntity);
        builder.Entity<OciBlob>(BuildOciBlobEntity);
        builder.Entity<OciBlobLink>(BuildOciBlobLinkEntity);
        builder.Entity<OciUpload>(BuildOciUploadEntity);
        builder.Entity<Repository>(BuildRepositoryEntity);
        builder.Entity<Project>(BuildProjectEntity);
        builder.Entity<Group>(BuildGroupEntity);
        builder.Entity<GroupMember>(BuildGroupMemberEntity);
        builder.Entity<PermissionTarget>(BuildPermissionTargetEntity);
        builder.Entity<PermissionGrant>(BuildPermissionGrantEntity);
        builder.Entity<ProjectMember>(BuildProjectMemberEntity);
        builder.Entity<AccessAuditEntry>(BuildAccessAuditEntryEntity);
        builder.Entity<RetentionPolicy>(BuildRetentionPolicyEntity);
        builder.Entity<RetentionHold>(BuildRetentionHoldEntity);
        builder.Entity<ArtifactUsage>(BuildArtifactUsageEntity);
        builder.Entity<ReplicationConfig>(BuildReplicationConfigEntity);
        builder.Entity<ReplicationRun>(BuildReplicationRunEntity);
        builder.Entity<ReplicationItem>(BuildReplicationItemEntity);
        builder.Entity<DataProtectionKeyEntry>(key =>
        {
            key.HasKey(k => k.Key);
            key.Property(k => k.FriendlyName).HasMaxLength(DataProtectionKeyEntry.MaxNameLength);
        });

        // Repository isolation: a query in one repository never sees another's rows. The filter reads the
        // context's current repository per query.
        builder.Entity<Package>().HasQueryFilter(e => e.RepositoryKey == CurrentRepositoryKey);
        builder.Entity<PackageOwner>().HasQueryFilter(e => e.RepositoryKey == CurrentRepositoryKey);
        builder.Entity<CatalogEntry>().HasQueryFilter(e => e.RepositoryKey == CurrentRepositoryKey);
        builder.Entity<PackageValidation>().HasQueryFilter(e => e.RepositoryKey == CurrentRepositoryKey);
        builder.Entity<AbuseReport>().HasQueryFilter(e => e.RepositoryKey == CurrentRepositoryKey);
        builder.Entity<PackageSymbol>().HasQueryFilter(e => e.RepositoryKey == CurrentRepositoryKey);
        builder.Entity<ScanResult>().HasQueryFilter(e => e.RepositoryKey == CurrentRepositoryKey);
        builder.Entity<NpmPackage>().HasQueryFilter(e => e.RepositoryKey == CurrentRepositoryKey);
        builder.Entity<MavenArtifact>().HasQueryFilter(e => e.RepositoryKey == CurrentRepositoryKey);
        builder.Entity<OciRepository>().HasQueryFilter(e => e.RepositoryKey == CurrentRepositoryKey);
        builder.Entity<OciBlob>().HasQueryFilter(e => e.RepositoryKey == CurrentRepositoryKey);
        builder.Entity<RetentionHold>().HasQueryFilter(e => e.RepositoryKey == CurrentRepositoryKey);
        builder.Entity<ArtifactUsage>().HasQueryFilter(e => e.RepositoryKey == CurrentRepositoryKey);

        // The column default is what the upgrade migration gives every row that predates repositories: they
        // belong to the default repository.
        builder.Entity<Package>().Property(e => e.RepositoryKey).HasDefaultValue(Repository.DefaultKey);
        builder.Entity<PackageOwner>().Property(e => e.RepositoryKey).HasDefaultValue(Repository.DefaultKey);
        builder.Entity<CatalogEntry>().Property(e => e.RepositoryKey).HasDefaultValue(Repository.DefaultKey);
        builder.Entity<PackageValidation>().Property(e => e.RepositoryKey).HasDefaultValue(Repository.DefaultKey);
        builder.Entity<AbuseReport>().Property(e => e.RepositoryKey).HasDefaultValue(Repository.DefaultKey);
        builder.Entity<PackageSymbol>().Property(e => e.RepositoryKey).HasDefaultValue(Repository.DefaultKey);
        builder.Entity<ScanResult>().Property(e => e.RepositoryKey).HasDefaultValue(Repository.DefaultKey);
        builder.Entity<NpmPackage>().Property(e => e.RepositoryKey).HasDefaultValue(Repository.DefaultKey);
        builder.Entity<MavenArtifact>().Property(e => e.RepositoryKey).HasDefaultValue(Repository.DefaultKey);
        builder.Entity<OciRepository>().Property(e => e.RepositoryKey).HasDefaultValue(Repository.DefaultKey);
        builder.Entity<OciBlob>().Property(e => e.RepositoryKey).HasDefaultValue(Repository.DefaultKey);
        builder.Entity<RetentionHold>().Property(e => e.RepositoryKey).HasDefaultValue(Repository.DefaultKey);
        builder.Entity<ArtifactUsage>().Property(e => e.RepositoryKey).HasDefaultValue(Repository.DefaultKey);

        BuildOwnershipForeignKeys(builder);
    }

    /// <summary>Entities whose <c>RepositoryKey</c> is a required foreign key to <see cref="Repository"/>.</summary>
    internal static readonly Type[] RepositoryOwned =
    {
        typeof(Package), typeof(PackageOwner), typeof(CatalogEntry), typeof(PackageValidation), typeof(AbuseReport),
        typeof(PackageSymbol), typeof(NpmPackage), typeof(MavenArtifact), typeof(OciRepository), typeof(OciBlob),
        typeof(ScanResult), typeof(RetentionHold), typeof(ArtifactUsage), typeof(ApiKeyRepository), typeof(ReplicationConfig),
    };

    /// <summary>Entities whose nullable <c>RepositoryKey</c> is an optional foreign key to <see cref="Repository"/>.</summary>
    internal static readonly Type[] RepositoryOptional = { typeof(RetentionPolicy), typeof(ScanPolicy) };

    /// <summary>Entities whose nullable <c>ProjectKey</c> is an optional foreign key to <see cref="Project"/>.</summary>
    internal static readonly Type[] ProjectOptional = { typeof(Repository), typeof(PermissionTarget), typeof(RetentionPolicy), typeof(ScanPolicy) };

    /// <summary>Entities whose <c>UserId</c> is a required foreign key to the Identity users table.</summary>
    internal static readonly Type[] UserOwned = { typeof(ApiKey), typeof(GroupMember), typeof(OrganizationMember) };

    // The foreign keys that tie a row to its repository, project, user or replication configuration. They are all Restrict and
    // have no navigation property (the entities are public API): the application deletes the owned rows itself, in order
    // (RepositoryAdministration, AccountDeletionService, ...), and the database refuses a parent that still has children.
    // The upgrade migration (AddOwnershipForeignKeys) deletes the rows that already pointed at nothing before adding them.
    // PermissionGrant/ProjectMember.PrincipalId is polymorphic (user or group, by PrincipalType), so it has none.
    private static void BuildOwnershipForeignKeys(ModelBuilder builder)
    {
        foreach (var type in RepositoryOwned)
        {
            builder.Entity(type).HasOne(typeof(Repository)).WithMany().HasForeignKey("RepositoryKey").IsRequired().OnDelete(DeleteBehavior.Restrict);
        }

        foreach (var type in RepositoryOptional)
        {
            builder.Entity(type).HasOne(typeof(Repository)).WithMany().HasForeignKey("RepositoryKey").IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        }

        foreach (var type in ProjectOptional)
        {
            builder.Entity(type).HasOne(typeof(Project)).WithMany().HasForeignKey("ProjectKey").IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        }

        foreach (var type in UserOwned)
        {
            builder.Entity(type).HasOne(typeof(BaGetUser)).WithMany().HasForeignKey("UserId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        }

        builder.Entity<ReplicationRun>().HasOne<ReplicationConfig>().WithMany().HasForeignKey(r => r.ConfigKey).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ReplicationItem>().HasOne<ReplicationConfig>().WithMany().HasForeignKey(i => i.ConfigKey).IsRequired().OnDelete(DeleteBehavior.Restrict);
        // NoAction, not Restrict: deleting an image cascades to its manifests and from them to the tags, and a tag also points at the
        // image, so the check has to wait for the end of the statement (PostgreSQL's RESTRICT is immediate). The code deletes the tags first anyway.
        builder.Entity<OciTag>().HasOne<OciRepository>().WithMany().HasForeignKey(t => t.ImageKey).IsRequired().OnDelete(DeleteBehavior.NoAction);
    }

    private void BuildRetentionPolicyEntity(EntityTypeBuilder<RetentionPolicy> policy)
    {
        policy.HasKey(p => p.Key);
        policy.HasIndex(p => p.Name).IsUnique();
        policy.HasIndex(p => p.RepositoryKey);
        policy.HasIndex(p => p.ProjectKey);

        policy.Property(p => p.Name).HasMaxLength(MaxAccessNameLength).IsRequired();
        policy.Property(p => p.NamePatterns).HasMaxLength(MaxRetentionPatternsLength);
        policy.Property(p => p.LastResult).HasMaxLength(MaxRetentionResultLength);
        policy.Property(p => p.UnusedSince);
    }

    private void BuildRetentionHoldEntity(EntityTypeBuilder<RetentionHold> hold)
    {
        hold.HasKey(h => h.Key);
        hold.HasIndex(h => new { h.RepositoryKey, h.Name, h.Version }).IsUnique();

        hold.Property(h => h.Name).HasMaxLength(MaxRetentionNameLength).IsRequired();
        // Empty = every version, so the column is never null (a unique index treats nulls as distinct on some engines).
        hold.Property(h => h.Version).HasMaxLength(MaxRetentionVersionLength).IsRequired();
        hold.Property(h => h.Reason).HasMaxLength(MaxRetentionReasonLength);
        hold.Property(h => h.CreatedBy).HasMaxLength(MaxAccountNameLength);
    }

    private void BuildReplicationConfigEntity(EntityTypeBuilder<ReplicationConfig> config)
    {
        config.HasKey(c => c.Key);
        config.HasIndex(c => c.Name).IsUnique();
        config.HasIndex(c => c.RepositoryKey);

        config.Property(c => c.Name).HasMaxLength(MaxAccessNameLength).IsRequired();
        config.Property(c => c.PeerUrl).HasMaxLength(ReplicationLimits.MaxUrlLength).IsRequired();
        config.Property(c => c.PeerRepository).HasMaxLength(ReplicationLimits.MaxPartLength).IsRequired();
        config.Property(c => c.ProtectedApiKey).HasMaxLength(ReplicationLimits.MaxSecretLength);
        config.Property(c => c.LastResult).HasMaxLength(ReplicationLimits.MaxResultLength);
        config.Property(c => c.Direction).HasConversion<int>();
        config.Property(c => c.Conflict).HasConversion<int>();
    }

    private void BuildReplicationRunEntity(EntityTypeBuilder<ReplicationRun> run)
    {
        run.HasKey(r => r.Key);
        run.HasIndex(r => new { r.ConfigKey, r.Started });

        run.Property(r => r.Trigger).HasMaxLength(32);
        run.Property(r => r.Actor).HasMaxLength(MaxAccountNameLength);
        run.Property(r => r.Error).HasMaxLength(ReplicationLimits.MaxResultLength);
        run.Property(r => r.Detail).HasMaxLength(ReplicationLimits.MaxDetailLength);
        run.Property(r => r.Status).HasConversion<int>();
    }

    private void BuildReplicationItemEntity(EntityTypeBuilder<ReplicationItem> item)
    {
        item.HasKey(i => i.Key);
        // Not unique: names compare differently per engine collation (a Docker tag is case-sensitive), the ledger is kept unique by the engine.
        item.HasIndex(i => new { i.ConfigKey, i.Name, i.Version, i.Part });

        item.Property(i => i.Name).HasMaxLength(ReplicationLimits.MaxNameLength).IsRequired();
        item.Property(i => i.Version).HasMaxLength(ReplicationLimits.MaxVersionLength).IsRequired();
        // Empty = not a Maven file, so the column is never null (a unique index treats nulls as distinct on some engines).
        item.Property(i => i.Part).HasMaxLength(ReplicationLimits.MaxPartLength).IsRequired();
        item.Property(i => i.Digest).HasMaxLength(ReplicationLimits.MaxDigestLength);
    }

    private void BuildArtifactUsageEntity(EntityTypeBuilder<ArtifactUsage> usage)
    {
        usage.HasKey(u => u.Key);
        usage.HasIndex(u => new { u.RepositoryKey, u.Name, u.Version }).IsUnique();

        usage.Property(u => u.Name).HasMaxLength(MaxRetentionNameLength).IsRequired();
        usage.Property(u => u.Version).HasMaxLength(MaxRetentionVersionLength).IsRequired();
    }

    private void BuildScanResultEntity(EntityTypeBuilder<ScanResult> result)
    {
        result.HasKey(r => r.Key);
        result.HasIndex(r => new { r.RepositoryKey, r.Name, r.Version }).IsUnique();

        result.Property(r => r.Name).HasMaxLength(MaxScanNameLength).IsRequired();
        result.Property(r => r.Version).HasMaxLength(MaxScanVersionLength).IsRequired();
        result.Property(r => r.Scanner).HasMaxLength(MaxScanScannerLength).IsRequired();
        result.Property(r => r.Error).HasMaxLength(MaxScanErrorLength);

        result.HasMany(r => r.Findings).WithOne().HasForeignKey(f => f.ResultKey).IsRequired().OnDelete(DeleteBehavior.Cascade);
    }

    private void BuildScanFindingEntity(EntityTypeBuilder<ScanFinding> finding)
    {
        finding.HasKey(f => f.Key);
        finding.HasIndex(f => f.ResultKey);

        finding.Property(f => f.VulnerabilityId).HasMaxLength(MaxScanIdLength).IsRequired();
        finding.Property(f => f.Component).HasMaxLength(MaxScanNameLength).IsRequired();
        finding.Property(f => f.ComponentVersion).HasMaxLength(MaxScanVersionLength).IsRequired();
        finding.Property(f => f.Summary).HasMaxLength(MaxScanSummaryLength);
        finding.Property(f => f.AdvisoryUrl).HasMaxLength(MaxScanUrlLength);
        finding.Property(f => f.FixedVersion).HasMaxLength(MaxScanVersionLength);
    }

    private void BuildScanPolicyEntity(EntityTypeBuilder<ScanPolicy> policy)
    {
        policy.HasKey(p => p.Key);
        // One policy per repository and one per project; separate indexes because a unique index over two nullable columns
        // would not constrain the rows where the other one is null on every provider.
        policy.HasIndex(p => p.RepositoryKey).IsUnique();
        policy.HasIndex(p => p.ProjectKey).IsUnique();

        policy.Property(p => p.ModifiedBy).HasMaxLength(MaxAccountNameLength);
    }

    private void BuildNpmPackageEntity(EntityTypeBuilder<NpmPackage> package)
    {
        package.HasKey(p => p.Key);
        package.HasIndex(p => new { p.RepositoryKey, p.Name }).IsUnique();

        package.Property(p => p.Name).HasMaxLength(MaxNpmNameLength).IsRequired();
    }

    private void BuildNpmVersionEntity(EntityTypeBuilder<NpmVersion> version)
    {
        version.HasKey(v => v.Key);
        version.HasIndex(v => new { v.PackageKey, v.Version }).IsUnique();

        version.Property(v => v.Version).HasMaxLength(MaxNpmVersionLength).IsRequired();
        version.Property(v => v.Integrity).HasMaxLength(MaxNpmIntegrityLength).IsRequired();
        version.Property(v => v.Shasum).HasMaxLength(MaxNpmShasumLength).IsRequired();
        version.Property(v => v.Manifest).IsRequired();

        // New tables, so the cascade is safe to declare here (unlike the older optional relationships).
        version.HasOne<NpmPackage>().WithMany().HasForeignKey(v => v.PackageKey).IsRequired().OnDelete(DeleteBehavior.Cascade);
    }

    private void BuildNpmDistTagEntity(EntityTypeBuilder<NpmDistTag> tag)
    {
        tag.HasKey(t => t.Key);
        tag.HasIndex(t => new { t.PackageKey, t.Tag }).IsUnique();

        tag.Property(t => t.Tag).HasMaxLength(MaxNpmTagLength).IsRequired();
        tag.Property(t => t.Version).HasMaxLength(MaxNpmVersionLength).IsRequired();

        tag.HasOne<NpmPackage>().WithMany().HasForeignKey(t => t.PackageKey).IsRequired().OnDelete(DeleteBehavior.Cascade);
    }

    private void BuildMavenArtifactEntity(EntityTypeBuilder<MavenArtifact> artifact)
    {
        artifact.HasKey(a => a.Key);
        artifact.HasIndex(a => new { a.RepositoryKey, a.GroupId, a.ArtifactId }).IsUnique();

        artifact.Property(a => a.GroupId).HasMaxLength(MaxMavenGroupLength).IsRequired();
        artifact.Property(a => a.ArtifactId).HasMaxLength(MaxMavenArtifactLength).IsRequired();
    }

    private void BuildMavenVersionEntity(EntityTypeBuilder<MavenVersion> version)
    {
        version.HasKey(v => v.Key);
        version.HasIndex(v => new { v.ArtifactKey, v.Version }).IsUnique();

        version.Property(v => v.Version).HasMaxLength(MaxMavenVersionLength).IsRequired();
        version.Property(v => v.Name).HasMaxLength(MaxMavenNameLength);
        version.Property(v => v.Description).HasMaxLength(MaxMavenDescriptionLength);
        version.Property(v => v.Packaging).HasMaxLength(MaxMavenPackagingLength);

        version.HasOne<MavenArtifact>().WithMany().HasForeignKey(v => v.ArtifactKey).IsRequired().OnDelete(DeleteBehavior.Cascade);
    }

    private void BuildMavenFileEntity(EntityTypeBuilder<MavenFile> file)
    {
        file.HasKey(f => f.Key);
        file.HasIndex(f => new { f.VersionKey, f.FileName }).IsUnique();

        file.Property(f => f.FileName).HasMaxLength(MaxMavenFileNameLength).IsRequired();
        file.Property(f => f.Classifier).HasMaxLength(MaxMavenClassifierLength);
        file.Property(f => f.Extension).HasMaxLength(MaxMavenExtensionLength).IsRequired();
        file.Property(f => f.SnapshotTimestamp).HasMaxLength(MaxMavenSnapshotTimestampLength);
        file.Property(f => f.Sha1).HasMaxLength(40).IsRequired();
        file.Property(f => f.Md5).HasMaxLength(32).IsRequired();
        file.Property(f => f.Sha256).HasMaxLength(64).IsRequired();
        file.Property(f => f.Sha512).HasMaxLength(MaxMavenChecksumLength).IsRequired();

        file.HasOne<MavenVersion>().WithMany().HasForeignKey(f => f.VersionKey).IsRequired().OnDelete(DeleteBehavior.Cascade);
    }

    private void BuildOciRepositoryEntity(EntityTypeBuilder<OciRepository> image)
    {
        image.HasKey(i => i.Key);
        image.HasIndex(i => new { i.RepositoryKey, i.Name }).IsUnique();

        image.Property(i => i.Name).HasMaxLength(MaxOciImageLength).IsRequired();
    }

    private void BuildOciManifestEntity(EntityTypeBuilder<OciManifest> manifest)
    {
        manifest.HasKey(m => m.Key);
        manifest.HasIndex(m => new { m.ImageKey, m.Digest }).IsUnique();
        manifest.HasIndex(m => new { m.ImageKey, m.SubjectDigest });

        manifest.Property(m => m.Digest).HasMaxLength(MaxOciDigestLength).IsRequired();
        manifest.Property(m => m.MediaType).HasMaxLength(MaxOciMediaTypeLength).IsRequired();
        manifest.Property(m => m.ArtifactType).HasMaxLength(MaxOciMediaTypeLength);
        manifest.Property(m => m.SubjectDigest).HasMaxLength(MaxOciDigestLength);
        // The pushed bytes, unbounded text on every provider (a manifest is at most Oci:MaxManifestBytes).
        manifest.Property(m => m.Content).IsRequired();

        manifest.HasOne<OciRepository>().WithMany().HasForeignKey(m => m.ImageKey).IsRequired().OnDelete(DeleteBehavior.Cascade);
    }

    private void BuildOciTagEntity(EntityTypeBuilder<OciTag> tag)
    {
        tag.HasKey(t => t.Key);
        tag.HasIndex(t => new { t.ImageKey, t.Name }).IsUnique();
        tag.HasIndex(t => t.ManifestKey);

        tag.Property(t => t.Name).HasMaxLength(MaxOciTagLength).IsRequired();

        // The image is a NoAction foreign key (BuildOwnershipForeignKeys): deleting an image reaches its tags through its manifests
        // (one cascade path, which SQL Server requires).
        tag.HasOne<OciManifest>().WithMany().HasForeignKey(t => t.ManifestKey).IsRequired().OnDelete(DeleteBehavior.Cascade);
    }

    private void BuildOciBlobEntity(EntityTypeBuilder<OciBlob> blob)
    {
        blob.HasKey(b => b.Key);
        blob.HasIndex(b => new { b.RepositoryKey, b.Digest }).IsUnique();

        blob.Property(b => b.Digest).HasMaxLength(MaxOciDigestLength).IsRequired();
    }

    private void BuildOciBlobLinkEntity(EntityTypeBuilder<OciBlobLink> link)
    {
        link.HasKey(l => l.Key);
        link.HasIndex(l => new { l.ImageKey, l.BlobKey }).IsUnique();
        link.HasIndex(l => l.BlobKey);

        // Deleting an image deletes its links; the blob side restricts (a blob row is removed only after its links, in code),
        // since two cascade paths to one table are refused by SQL Server.
        link.HasOne<OciRepository>().WithMany().HasForeignKey(l => l.ImageKey).IsRequired().OnDelete(DeleteBehavior.Cascade);
        link.HasOne<OciBlob>().WithMany().HasForeignKey(l => l.BlobKey).IsRequired().OnDelete(DeleteBehavior.Restrict);
    }

    private void BuildOciUploadEntity(EntityTypeBuilder<OciUpload> upload)
    {
        upload.HasKey(u => u.Key);
        upload.HasIndex(u => u.Reference).IsUnique();
        upload.HasIndex(u => u.ImageKey);

        upload.Property(u => u.Reference).HasMaxLength(MaxOciUploadReferenceLength).IsRequired();
        upload.Property(u => u.StorageUploadId).HasMaxLength(MaxOciStorageUploadIdLength).IsRequired();

        upload.HasOne<OciRepository>().WithMany().HasForeignKey(u => u.ImageKey).IsRequired().OnDelete(DeleteBehavior.Cascade);
    }

    private void BuildRepositoryEntity(EntityTypeBuilder<Repository> repository)
    {
        repository.HasKey(r => r.Key);
        repository.HasIndex(r => r.Name).IsUnique();

        repository.Property(r => r.Name).HasMaxLength(MaxRepositoryNameLength).IsRequired();
        repository.Property(r => r.Format).HasConversion<string>().HasMaxLength(MaxRepositoryFormatLength);
        repository.Property(r => r.Kind).HasConversion<string>().HasMaxLength(MaxRepositoryFormatLength);
    }

    private void BuildGroupEntity(EntityTypeBuilder<Group> group)
    {
        group.HasKey(g => g.Key);
        group.HasIndex(g => g.Name).IsUnique();

        group.Property(g => g.Name).HasMaxLength(MaxAccessNameLength).IsRequired();
        group.Property(g => g.Description).HasMaxLength(MaxAccessDescriptionLength);
    }

    private void BuildGroupMemberEntity(EntityTypeBuilder<GroupMember> member)
    {
        member.HasKey(m => m.Key);
        member.HasIndex(m => new { m.GroupKey, m.UserId }).IsUnique();
        member.HasIndex(m => m.UserId);

        member.Property(m => m.UserId).HasMaxLength(MaxUserIdLength).IsRequired();

        // A new table, so the cascade is safe to declare here (unlike the older optional relationships).
        member.HasOne(m => m.Group).WithMany().HasForeignKey(m => m.GroupKey).IsRequired().OnDelete(DeleteBehavior.Cascade);
    }

    private void BuildPermissionTargetEntity(EntityTypeBuilder<PermissionTarget> target)
    {
        target.HasKey(t => t.Key);
        target.HasIndex(t => t.Name).IsUnique();

        target.Property(t => t.Name).HasMaxLength(MaxAccessNameLength).IsRequired();

        #pragma warning disable CS8620
        foreach (var patterns in new[] { target.Property(t => t.RepositoryPatterns), target.Property(t => t.IncludePatterns), target.Property(t => t.ExcludePatterns) })
        {
            patterns
                .HasMaxLength(DefaultMaxStringLength)
                .HasConversion(StringArrayToJsonConverter.Instance)
                .Metadata.SetValueComparer(StringArrayComparer.Instance);
        }
        #pragma warning restore CS8620
    }

    private void BuildPermissionGrantEntity(EntityTypeBuilder<PermissionGrant> grant)
    {
        grant.HasKey(g => g.Key);
        grant.HasIndex(g => new { g.TargetKey, g.PrincipalType, g.PrincipalId }).IsUnique();

        grant.Property(g => g.PrincipalType).HasConversion<string>().HasMaxLength(MaxPrincipalTypeLength);
        grant.Property(g => g.PrincipalId).HasMaxLength(MaxUserIdLength).IsRequired();

        grant.HasOne(g => g.Target).WithMany().HasForeignKey(g => g.TargetKey).IsRequired().OnDelete(DeleteBehavior.Cascade);
    }

    private void BuildProjectMemberEntity(EntityTypeBuilder<ProjectMember> member)
    {
        member.HasKey(m => m.Key);
        member.HasIndex(m => new { m.ProjectKey, m.PrincipalType, m.PrincipalId }).IsUnique();

        member.Property(m => m.PrincipalType).HasConversion<string>().HasMaxLength(MaxPrincipalTypeLength);
        member.Property(m => m.PrincipalId).HasMaxLength(MaxUserIdLength).IsRequired();
        member.Property(m => m.Role).HasConversion<string>().HasMaxLength(MaxPrincipalTypeLength);

        member.HasOne(m => m.Project).WithMany().HasForeignKey(m => m.ProjectKey).IsRequired().OnDelete(DeleteBehavior.Cascade);
    }

    private void BuildAccessAuditEntryEntity(EntityTypeBuilder<AccessAuditEntry> entry)
    {
        entry.HasKey(e => e.Key);
        entry.HasIndex(e => e.Created);

        entry.Property(e => e.Actor).HasMaxLength(MaxAccountNameLength);
        entry.Property(e => e.Action).HasMaxLength(MaxAuditActionLength).IsRequired();
        entry.Property(e => e.Subject).HasMaxLength(MaxAccountNameLength);
        entry.Property(e => e.Details).HasMaxLength(DefaultMaxStringLength);
    }

    private void BuildProjectEntity(EntityTypeBuilder<Project> project)
    {
        project.HasKey(p => p.Key);
        project.HasIndex(p => p.Name).IsUnique();

        project.Property(p => p.Name).HasMaxLength(MaxProjectNameLength).IsRequired();
        project.Property(p => p.DisplayName).HasMaxLength(MaxProjectDisplayNameLength);
    }

    private void BuildPackageEntity(EntityTypeBuilder<Package> package)
    {
        package.HasKey(p => p.Key);
        package.HasIndex(p => p.Id);
        package.HasIndex(p => new { p.RepositoryKey, p.Id, p.NormalizedVersionString })
            .IsUnique();

        package.Property(p => p.Id)
            .HasMaxLength(MaxPackageIdLength)
            .IsRequired();

        package.Property(p => p.NormalizedVersionString)
            .HasColumnName("Version")
            .HasMaxLength(MaxPackageVersionLength)
            .IsRequired();

        package.Property(p => p.OriginalVersionString)
            .HasColumnName("OriginalVersion")
            .HasMaxLength(MaxPackageVersionLength);

        package.Property(p => p.ReleaseNotes)
            .HasColumnName("ReleaseNotes");

        #pragma warning disable CS8620
        package.Property(p => p.Authors)
            .HasMaxLength(DefaultMaxStringLength)
            .HasConversion(StringArrayToJsonConverter.Instance)
            .Metadata.SetValueComparer(StringArrayComparer.Instance);
        #pragma warning restore CS8620

        #pragma warning disable CS8620
        package.Property(p => p.IconUrl)
            .HasConversion(UriToStringConverter.Instance)
            .HasMaxLength(DefaultMaxStringLength);
        #pragma warning restore CS8620

        #pragma warning disable CS8620
        package.Property(p => p.LicenseUrl)
            .HasConversion(UriToStringConverter.Instance)
            .HasMaxLength(DefaultMaxStringLength);
        #pragma warning restore CS8620

        #pragma warning disable CS8620
        package.Property(p => p.ProjectUrl)
            .HasConversion(UriToStringConverter.Instance)
            .HasMaxLength(DefaultMaxStringLength);
        #pragma warning restore CS8620

        #pragma warning disable CS8620
        package.Property(p => p.RepositoryUrl)
            .HasConversion(UriToStringConverter.Instance)
            .HasMaxLength(DefaultMaxStringLength);
        #pragma warning restore CS8620

        #pragma warning disable CS8620
        package.Property(p => p.Tags)
            .HasMaxLength(DefaultMaxStringLength)
            .HasConversion(StringArrayToJsonConverter.Instance)
            .Metadata.SetValueComparer(StringArrayComparer.Instance);
        #pragma warning restore CS8620

        package.Property(p => p.Description).HasMaxLength(DefaultMaxStringLength);
        package.Property(p => p.Language).HasMaxLength(MaxPackageLanguageLength);
        package.Property(p => p.MinClientVersion).HasMaxLength(MaxPackageMinClientVersionLength);
        package.Property(p => p.Summary).HasMaxLength(DefaultMaxStringLength);
        package.Property(p => p.Title).HasMaxLength(MaxPackageTitleLength);
        package.Property(p => p.RepositoryType).HasMaxLength(MaxRepositoryTypeLength);

        package.Ignore(p => p.Version);
        package.Ignore(p => p.IconUrlString);
        package.Ignore(p => p.LicenseUrlString);
        package.Ignore(p => p.ProjectUrlString);
        package.Ignore(p => p.RepositoryUrlString);

        // The dependency to package relationship is deliberately left optional. Making it
        // required, or giving it a cascade delete, changes the foreign key and so needs a
        // migration; SQLite cannot alter a foreign key, so consumers would have to
        // recreate their database and reindex every package. PackageDatabase therefore
        // deletes a package's dependency rows itself, so that none are left orphaned.
        // See: https://github.com/loic-sharma/BaGet/pull/174

        package.HasMany(p => p.PackageTypes)
            .WithOne(d => d.Package)
            .IsRequired();

        package.HasMany(p => p.TargetFrameworks)
            .WithOne(d => d.Package)
            .IsRequired();

        package.Property(p => p.RowVersion).IsRowVersion();
    }

    private void BuildPackageDeprecationEntity(EntityTypeBuilder<PackageDeprecation> deprecation)
    {
        deprecation.HasKey(d => d.Key);

        #pragma warning disable CS8620
        deprecation
            .Property(d => d.Reasons)
            .HasMaxLength(DefaultMaxStringLength)
            .HasConversion(StringArrayToJsonConverter.Instance)
            .Metadata.SetValueComparer(StringArrayComparer.Instance);
        #pragma warning restore CS8620

        deprecation.Property(d => d.Message).HasMaxLength(DefaultMaxStringLength);
        deprecation.Property(d => d.AlternatePackageId).HasMaxLength(MaxPackageIdLength);
        deprecation.Property(d => d.AlternateVersionRange).HasMaxLength(MaxPackageDependencyVersionRangeLength);

        // A version has at most one deprecation. The relationship is optional in the
        // database for the same reason as the dependency one: a required relationship
        // would need a foreign key change, which SQLite cannot make in place.
        // The foreign key is declared explicitly: for a one to one relationship EF
        // cannot otherwise tell which side is the dependent.
        deprecation
            .HasOne(d => d.Package)
            .WithOne(p => p.Deprecation)
            .HasForeignKey<PackageDeprecation>(d => d.PackageKey)
            .IsRequired(false);
    }

    private void BuildPackageVulnerabilityEntity(EntityTypeBuilder<PackageVulnerability> vulnerability)
    {
        vulnerability.HasKey(v => v.Key);

        vulnerability.Property(v => v.AdvisoryUrl).HasMaxLength(DefaultMaxStringLength);
        vulnerability.Property(v => v.VersionRange).HasMaxLength(MaxPackageDependencyVersionRangeLength);
        vulnerability.Property(v => v.Source).HasMaxLength(MaxScanScannerLength);

        vulnerability
            .HasOne(v => v.Package)
            .WithMany(p => p.Vulnerabilities)
            .HasForeignKey(v => v.PackageKey)
            .IsRequired(false);
    }

    private void BuildCatalogEntryEntity(EntityTypeBuilder<CatalogEntry> entry)
    {
        entry.HasKey(e => e.Key);
        entry.HasIndex(e => e.CommitId).IsUnique();
        entry.HasIndex(e => e.RepositoryKey);

        entry.Property(e => e.CommitId).HasMaxLength(MaxCatalogCommitIdLength).IsRequired();
        entry.Property(e => e.Type).HasMaxLength(MaxCatalogTypeLength).IsRequired();
        entry.Property(e => e.PackageId).HasMaxLength(MaxPackageIdLength).IsRequired();
        entry.Property(e => e.PackageVersion).HasMaxLength(MaxPackageVersionLength).IsRequired();
    }

    private void BuildOrganizationEntity(EntityTypeBuilder<Organization> organization)
    {
        organization.HasKey(o => o.Key);
        organization.HasIndex(o => o.Name).IsUnique();

        organization.Property(o => o.Name).HasMaxLength(MaxAccountNameLength).IsRequired();
    }

    private void BuildOrganizationMemberEntity(EntityTypeBuilder<OrganizationMember> member)
    {
        member.HasKey(m => m.Key);
        member.HasIndex(m => m.UserId);

        member.Property(m => m.UserId).HasMaxLength(MaxUserIdLength).IsRequired();

        member
            .HasOne(m => m.Organization)
            .WithMany()
            .HasForeignKey(m => m.OrganizationKey)
            .IsRequired(false);
    }

    private void BuildPackageOwnerEntity(EntityTypeBuilder<PackageOwner> owner)
    {
        owner.HasKey(o => o.Key);
        owner.HasIndex(o => new { o.RepositoryKey, o.PackageId });
        owner.HasIndex(o => o.OwnerName);

        owner.Property(o => o.PackageId).HasMaxLength(MaxPackageIdLength).IsRequired();
        owner.Property(o => o.OwnerName).HasMaxLength(MaxAccountNameLength).IsRequired();
    }

    private void BuildApiKeyEntity(EntityTypeBuilder<ApiKey> apiKey)
    {
        apiKey.HasKey(k => k.Key);
        apiKey.HasIndex(k => k.HashedKey).IsUnique();
        apiKey.HasIndex(k => k.UserId);

        apiKey.Property(k => k.UserId).HasMaxLength(MaxUserIdLength).IsRequired();
        apiKey.Property(k => k.Description).HasMaxLength(MaxApiKeyDescriptionLength);
        apiKey.Property(k => k.HashedKey).HasMaxLength(MaxApiKeyHashLength).IsRequired();
        apiKey.Property(k => k.PackageGlobPattern).HasMaxLength(MaxPackageIdLength);

        #pragma warning disable CS8620
        apiKey
            .Property(k => k.Scopes)
            .HasMaxLength(DefaultMaxStringLength)
            .HasConversion(StringArrayToJsonConverter.Instance)
            .Metadata.SetValueComparer(StringArrayComparer.Instance);
        #pragma warning restore CS8620
    }

    private void BuildApiKeyRepositoryEntity(EntityTypeBuilder<ApiKeyRepository> row)
    {
        row.HasKey(r => r.Key);
        row.HasIndex(r => new { r.ApiKeyKey, r.RepositoryKey }).IsUnique();
        row.HasIndex(r => r.RepositoryKey);

        // A revoked or deleted key takes its rows with it, in the database too.
        row.HasOne(r => r.ApiKey).WithMany().HasForeignKey(r => r.ApiKeyKey).OnDelete(DeleteBehavior.Cascade);
    }

    private void BuildPackageValidationEntity(EntityTypeBuilder<PackageValidation> validation)
    {
        validation.HasKey(v => v.Key);
        validation.HasIndex(v => v.SubmissionId);
        validation.HasIndex(v => new { v.RepositoryKey, v.PackageId });

        validation.Property(v => v.SubmissionId).HasMaxLength(MaxCatalogCommitIdLength).IsRequired();
        validation.Property(v => v.PackageId).HasMaxLength(MaxPackageIdLength).IsRequired();
        validation.Property(v => v.PackageVersion).HasMaxLength(MaxPackageVersionLength).IsRequired();
        validation.Property(v => v.Type).HasMaxLength(MaxValidationTypeLength).IsRequired();
        validation.Property(v => v.Message).HasMaxLength(DefaultMaxStringLength);

        validation
            .HasOne(v => v.Package)
            .WithMany()
            .HasForeignKey(v => v.PackageKey)
            .IsRequired(false);
    }

    private void BuildAbuseReportEntity(EntityTypeBuilder<AbuseReport> report)
    {
        report.HasKey(r => r.Key);
        report.HasIndex(r => new { r.RepositoryKey, r.PackageId });

        report.Property(r => r.PackageId).HasMaxLength(MaxPackageIdLength).IsRequired();
        report.Property(r => r.PackageVersion).HasMaxLength(MaxPackageVersionLength).IsRequired();
        report.Property(r => r.Reason).HasMaxLength(MaxAbuseReasonLength).IsRequired();
        report.Property(r => r.Details).HasMaxLength(DefaultMaxStringLength).IsRequired();
        report.Property(r => r.ReporterEmail).HasMaxLength(MaxEmailLength);
    }

    private void BuildPackageSymbolEntity(EntityTypeBuilder<PackageSymbol> symbol)
    {
        symbol.HasKey(s => s.Key);
        symbol.HasIndex(s => new { s.RepositoryKey, s.PackageId, s.PackageVersion });
        symbol.HasIndex(s => new { s.RepositoryKey, s.FileName, s.SymbolKey });

        symbol.Property(s => s.PackageId).HasMaxLength(MaxPackageIdLength).IsRequired();
        symbol.Property(s => s.PackageVersion).HasMaxLength(MaxPackageVersionLength).IsRequired();
        symbol.Property(s => s.FileName).HasMaxLength(MaxPdbFileNameLength).IsRequired();
        symbol.Property(s => s.SymbolKey).HasMaxLength(MaxSymbolKeyLength).IsRequired();
    }

    private void BuildPackageDependencyEntity(EntityTypeBuilder<PackageDependency> dependency)
    {
        dependency.HasKey(d => d.Key);
        dependency.HasIndex(d => d.Id);

        dependency.Property(d => d.Id).HasMaxLength(MaxPackageIdLength);
        dependency.Property(d => d.VersionRange).HasMaxLength(MaxPackageDependencyVersionRangeLength);
        dependency.Property(d => d.TargetFramework).HasMaxLength(MaxTargetFrameworkLength);
    }

    private void BuildPackageTypeEntity(EntityTypeBuilder<PackageType> type)
    {
        type.HasKey(d => d.Key);
        type.HasIndex(d => d.Name);

        type.Property(d => d.Name).HasMaxLength(MaxPackageTypeNameLength);
        type.Property(d => d.Version).HasMaxLength(MaxPackageTypeVersionLength);
    }

    private void BuildTargetFrameworkEntity(EntityTypeBuilder<TargetFramework> targetFramework)
    {
        targetFramework.HasKey(f => f.Key);
        targetFramework.HasIndex(f => f.Moniker);

        targetFramework.Property(f => f.Moniker).HasMaxLength(MaxTargetFrameworkLength);
    }
}
