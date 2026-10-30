using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BaGet.Database.Tests;

/// <summary>
/// A database just before the <c>AddOwnershipForeignKeys</c> migration, filled with rows that point at a repository, project,
/// user or replication configuration that does not exist (orphans, which nothing prevented) next to valid ones, on any
/// engine. The migration must delete the orphans (children first), detach what is optional and keep every valid row.
/// Linked into <c>BaGet.Tests</c> too, which runs it on SQLite.
/// </summary>
public static class OwnershipUpgradeDatabase
{
    public const int MissingRepository = 99;
    public const int MissingProject = 98;
    public const int MissingConfig = 97;
    public const string MissingUser = "user-ghost";
    public const string User = "user-ok";

    /// <summary>The id of the last migration before <c>AddOwnershipForeignKeys</c> in the context's provider.</summary>
    public static string LastBeforeForeignKeys(DbContext context)
    {
        var ids = context.GetService<IMigrationsAssembly>().Migrations.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var index = ids.FindIndex(id => id.EndsWith("_AddOwnershipForeignKeys", StringComparison.Ordinal));
        if (index < 1) throw new InvalidOperationException("The AddOwnershipForeignKeys migration was not found.");

        return ids[index - 1];
    }

    /// <summary>Creates the database at <paramref name="connectionString"/> just before the migration and fills it.</summary>
    public static async Task CreateAsync(IDatabaseContextFactory factory, string connectionString, DatabaseOptions options, CancellationToken token)
    {
        await using var context = (DbContext)factory.Create(connectionString, options);
        context.GetService<IMigrator>().Migrate(LastBeforeForeignKeys(context));

        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(token).ConfigureAwait(false);
        var seed = new PreRepositoryDatabase.Inserter(connection, options.Type ?? string.Empty);
        var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        await seed.InsertAsync("AspNetUsers", new()
        {
            ["Id"] = User, ["Created"] = now, ["UserName"] = "ok", ["NormalizedUserName"] = "OK",
            ["EmailConfirmed"] = false, ["PhoneNumberConfirmed"] = false, ["TwoFactorEnabled"] = false,
            ["LockoutEnabled"] = true, ["AccessFailedCount"] = 0, ["IsAdministrator"] = false, ["SecurityStamp"] = "stamp",
        }, token).ConfigureAwait(false);

        // Projects, repositories, targets.
        await seed.InsertAsync("Projects", new() { ["Name"] = "p-ok", ["Created"] = now }, token).ConfigureAwait(false);
        var project = await seed.MaxKeyAsync("Projects", token).ConfigureAwait(false);
        await RepositoryRow(seed, "kept", project, token).ConfigureAwait(false);
        await RepositoryRow(seed, "detached", MissingProject, token).ConfigureAwait(false);
        await Target(seed, "target-ok", project, token).ConfigureAwait(false);
        await Target(seed, "target-orphan", MissingProject, token).ConfigureAwait(false);

        // NuGet: a valid package, an orphan one with children and a validation that names it from a valid repository.
        foreach (var (id, repository) in new[] { ("Valid.Package", 1), ("Orphan.Package", MissingRepository) })
        {
            await seed.InsertAsync("Packages", new()
            {
                ["Id"] = id, ["Version"] = "1.0.0", ["Authors"] = "[]", ["Downloads"] = 0L, ["HasReadme"] = false, ["Listed"] = true,
                ["Published"] = now, ["RequireLicenseAcceptance"] = false, ["Tags"] = "[]", ["IsPrerelease"] = false, ["SemVerLevel"] = 0,
                ["OriginalVersion"] = "1.0.0", ["HasEmbeddedIcon"] = false, ["RepositoryKey"] = repository,
            }, token).ConfigureAwait(false);
            var key = await seed.PackageKeyAsync(id, "1.0.0", token).ConfigureAwait(false);
            await seed.InsertAsync("PackageTypes", new() { ["Name"] = "Dependency", ["Version"] = "0.0", ["PackageKey"] = key }, token).ConfigureAwait(false);
            await seed.InsertAsync("TargetFrameworks", new() { ["Moniker"] = "net8.0", ["PackageKey"] = key }, token).ConfigureAwait(false);
            await seed.InsertAsync("PackageDependencies", new() { ["Id"] = "Dep", ["VersionRange"] = "1.0.0", ["TargetFramework"] = "net8.0", ["PackageKey"] = key }, token).ConfigureAwait(false);
            await seed.InsertAsync("PackageValidations", new()
            {
                ["SubmissionId"] = Guid.NewGuid().ToString("D"), ["PackageId"] = id, ["PackageVersion"] = "1.0.0", ["Type"] = "Scan",
                ["State"] = 1, ["Created"] = now, ["PackageKey"] = key, ["RepositoryKey"] = 1,
            }, token).ConfigureAwait(false);
            await seed.InsertAsync("PackageOwners", new() { ["PackageId"] = id, ["OwnerName"] = "ok", ["IsVerified"] = false, ["RepositoryKey"] = repository }, token).ConfigureAwait(false);
        }

        // API keys: valid, of a missing user, limited only to a missing repository, limited to a valid one and a missing one.
        var keys = new Dictionary<string, int>();
        foreach (var (name, user) in new[] { ("k-ok", User), ("k-ghost", MissingUser), ("k-only-missing", User), ("k-mixed", User) })
        {
            await seed.InsertAsync("ApiKeys", new()
            {
                ["UserId"] = user, ["Description"] = name, ["HashedKey"] = name.PadRight(64, 'x'), ["Scopes"] = "[\"package:push\"]",
                ["PackageGlobPattern"] = "*", ["Created"] = now,
            }, token).ConfigureAwait(false);
            keys[name] = await seed.MaxKeyAsync("ApiKeys", token).ConfigureAwait(false);
        }

        foreach (var (name, repository) in new[] { ("k-only-missing", MissingRepository), ("k-mixed", 1), ("k-mixed", MissingRepository) })
        {
            await seed.InsertAsync("ApiKeyRepositories", new() { ["ApiKeyKey"] = keys[name], ["RepositoryKey"] = repository }, token).ConfigureAwait(false);
        }

        // Group memberships (the seeded group Everyone has key 1).
        await seed.InsertAsync("GroupMembers", new() { ["GroupKey"] = 1, ["UserId"] = User }, token).ConfigureAwait(false);
        await seed.InsertAsync("GroupMembers", new() { ["GroupKey"] = 1, ["UserId"] = MissingUser }, token).ConfigureAwait(false);

        // Policies: kept, repository gone, project gone, repository valid + project gone.
        foreach (var (name, repository, projectKey) in new (string, int?, int?)[]
                 { ("r-ok", 1, null), ("r-repo-gone", MissingRepository, null), ("r-project-gone", null, MissingProject), ("r-mixed", 1, MissingProject) })
        {
            await seed.InsertAsync("RetentionPolicies", new()
            {
                ["Name"] = name, ["Created"] = now, ["Modified"] = now, ["Enabled"] = false, ["PrereleaseOnly"] = false, ["CollectGarbage"] = false,
                ["RepositoryKey"] = repository, ["ProjectKey"] = projectKey,
            }, token).ConfigureAwait(false);
        }

        foreach (var (repository, projectKey) in new (int?, int?)[] { (1, null), (MissingRepository, null), (null, MissingProject) })
        {
            await seed.InsertAsync("ScanPolicies", new() { ["BlockUnscanned"] = false, ["Modified"] = now, ["RepositoryKey"] = repository, ["ProjectKey"] = projectKey }, token).ConfigureAwait(false);
        }

        // npm, OCI, scan results, replication.
        foreach (var (name, repository) in new[] { ("valid-npm", 1), ("orphan-npm", MissingRepository) })
        {
            await seed.InsertAsync("NpmPackages", new() { ["Name"] = name, ["Created"] = now, ["Modified"] = now, ["RepositoryKey"] = repository }, token).ConfigureAwait(false);
            var key = await seed.MaxKeyAsync("NpmPackages", token).ConfigureAwait(false);
            await seed.InsertAsync("NpmVersions", new()
            {
                ["PackageKey"] = key, ["Version"] = "1.0.0", ["Integrity"] = "sha512-x", ["Shasum"] = "x", ["Manifest"] = "{}", ["Published"] = now, ["TarballSize"] = 1L,
            }, token).ConfigureAwait(false);
            await seed.InsertAsync("NpmDistTags", new() { ["PackageKey"] = key, ["Tag"] = "latest", ["Version"] = "1.0.0" }, token).ConfigureAwait(false);
        }

        foreach (var (name, repository) in new[] { ("valid/image", 1), ("orphan/image", MissingRepository) })
        {
            await seed.InsertAsync("OciRepositories", new() { ["Name"] = name, ["Created"] = now, ["Modified"] = now, ["RepositoryKey"] = repository }, token).ConfigureAwait(false);
            var image = await seed.MaxKeyAsync("OciRepositories", token).ConfigureAwait(false);
            await seed.InsertAsync("OciManifests", new()
            {
                ["ImageKey"] = image, ["Digest"] = "sha256:" + name.Length.ToString("D64"), ["MediaType"] = "application/json", ["Content"] = "{}", ["Size"] = 2L, ["Created"] = now,
            }, token).ConfigureAwait(false);
            var manifest = await seed.MaxKeyAsync("OciManifests", token).ConfigureAwait(false);
            await seed.InsertAsync("OciTags", new() { ["ImageKey"] = image, ["ManifestKey"] = manifest, ["Name"] = "latest", ["Updated"] = now }, token).ConfigureAwait(false);
        }

        await seed.InsertAsync("OciBlobs", new() { ["Digest"] = "sha256:" + new string('a', 64), ["Size"] = 1L, ["RefCount"] = 0, ["Created"] = now, ["RepositoryKey"] = MissingRepository }, token).ConfigureAwait(false);

        foreach (var (name, repository) in new[] { ("valid-scan", 1), ("orphan-scan", MissingRepository) })
        {
            await seed.InsertAsync("ScanResults", new()
            {
                ["Name"] = name, ["Version"] = "1.0.0", ["Scanner"] = "osv", ["Attempted"] = now, ["FindingCount"] = 1, ["Highest"] = 1, ["RepositoryKey"] = repository,
            }, token).ConfigureAwait(false);
            var result = await seed.MaxKeyAsync("ScanResults", token).ConfigureAwait(false);
            await seed.InsertAsync("ScanFindings", new() { ["ResultKey"] = result, ["VulnerabilityId"] = "X-1", ["Component"] = "c", ["ComponentVersion"] = "1", ["Severity"] = 1 }, token).ConfigureAwait(false);
        }

        foreach (var (name, repository) in new[] { ("c-ok", 1), ("c-orphan", MissingRepository) })
        {
            await seed.InsertAsync("ReplicationConfigs", new()
            {
                ["Name"] = name, ["RepositoryKey"] = repository, ["Direction"] = 0, ["Conflict"] = 0, ["PeerUrl"] = "http://peer", ["PeerRepository"] = "default",
                ["Enabled"] = false, ["OnDeploy"] = false, ["PropagateDeletes"] = false, ["Created"] = now, ["Modified"] = now,
            }, token).ConfigureAwait(false);
            var config = await seed.MaxKeyAsync("ReplicationConfigs", token).ConfigureAwait(false);
            await RunRow(seed, config, now, token).ConfigureAwait(false);
            await seed.InsertAsync("ReplicationItems", new() { ["ConfigKey"] = config, ["Name"] = "n", ["Version"] = "1", ["Part"] = string.Empty, ["Synced"] = now }, token).ConfigureAwait(false);
        }

        // Maven, catalog, abuse reports, symbols, deprecations and vulnerabilities, holds, usage, blob links, upload sessions.
        foreach (var (name, repository) in new[] { ("valid", 1), ("orphan", MissingRepository) })
        {
            await seed.InsertAsync("MavenArtifacts", new() { ["GroupId"] = "g." + name, ["ArtifactId"] = "a", ["Created"] = now, ["Modified"] = now, ["RepositoryKey"] = repository }, token).ConfigureAwait(false);
            var artifact = await seed.MaxKeyAsync("MavenArtifacts", token).ConfigureAwait(false);
            await seed.InsertAsync("MavenVersions", new() { ["ArtifactKey"] = artifact, ["Version"] = "1.0", ["IsSnapshot"] = false, ["Created"] = now, ["Modified"] = now }, token).ConfigureAwait(false);
            var version = await seed.MaxKeyAsync("MavenVersions", token).ConfigureAwait(false);
            await seed.InsertAsync("MavenFiles", new()
            {
                ["VersionKey"] = version, ["FileName"] = "a-1.0.jar", ["Extension"] = "jar", ["Size"] = 1L, ["SnapshotBuild"] = 0, ["Created"] = now,
                ["Sha1"] = "x", ["Md5"] = "x", ["Sha256"] = "x", ["Sha512"] = "x",
            }, token).ConfigureAwait(false);
            await seed.InsertAsync("CatalogLeafs", new()
            {
                ["CommitId"] = Guid.NewGuid().ToString("D"), ["CommitTimestamp"] = now, ["Type"] = "nuget:PackageDetails", ["PackageId"] = name, ["PackageVersion"] = "1.0.0", ["RepositoryKey"] = repository,
            }, token).ConfigureAwait(false);
            await seed.InsertAsync("AbuseReports", new() { ["PackageId"] = name, ["PackageVersion"] = "1.0.0", ["Reason"] = "Other", ["Details"] = "d", ["Created"] = now, ["RepositoryKey"] = repository }, token).ConfigureAwait(false);
            await seed.InsertAsync("PackageSymbols", new() { ["PackageId"] = name, ["PackageVersion"] = "1.0.0", ["FileName"] = "a.pdb", ["SymbolKey"] = "k", ["Created"] = now, ["RepositoryKey"] = repository }, token).ConfigureAwait(false);
            await seed.InsertAsync("RetentionHolds", new() { ["Name"] = name, ["Version"] = string.Empty, ["Created"] = now, ["RepositoryKey"] = repository }, token).ConfigureAwait(false);
            await seed.InsertAsync("ArtifactUsages", new() { ["Name"] = name, ["Version"] = "1", ["LastUsed"] = now, ["RepositoryKey"] = repository }, token).ConfigureAwait(false);
        }

        foreach (var package in new[] { "Valid.Package", "Orphan.Package" })
        {
            var key = await seed.PackageKeyAsync(package, "1.0.0", token).ConfigureAwait(false);
            await seed.InsertAsync("PackageDeprecations", new() { ["Reasons"] = "[]", ["PackageKey"] = key }, token).ConfigureAwait(false);
            await seed.InsertAsync("PackageVulnerabilities", new() { ["Severity"] = 1, ["PackageKey"] = key }, token).ConfigureAwait(false);
        }

        // Image-level rows: a link and an upload session of the valid image and of the orphan one, both linking a valid blob.
        var images = new Dictionary<string, int>();
        foreach (var name in new[] { "valid/image", "orphan/image" })
        {
            using var lookup = connection.CreateCommand();
            lookup.CommandText = "SELECT " + seed.Quote("Key") + " FROM " + seed.Quote("OciRepositories") + " WHERE " + seed.Quote("Name") + " = '" + name + "'";
            images[name] = Convert.ToInt32(await lookup.ExecuteScalarAsync(token).ConfigureAwait(false));
        }

        await seed.InsertAsync("OciBlobs", new() { ["Digest"] = "sha256:" + new string('c', 64), ["Size"] = 1L, ["RefCount"] = 1, ["Created"] = now, ["RepositoryKey"] = 1 }, token).ConfigureAwait(false);
        var validBlob = await seed.MaxKeyAsync("OciBlobs", token).ConfigureAwait(false);
        foreach (var (image, name) in new[] { (images["valid/image"], "valid"), (images["orphan/image"], "orphan") })
        {
            await seed.InsertAsync("OciBlobLinks", new() { ["ImageKey"] = image, ["BlobKey"] = validBlob, ["Manifests"] = 0, ["Created"] = now }, token).ConfigureAwait(false);
            await seed.InsertAsync("OciUploads", new() { ["ImageKey"] = image, ["Reference"] = name, ["StorageUploadId"] = "u", ["Created"] = now, ["Updated"] = now }, token).ConfigureAwait(false);
        }

        // Organization memberships: of a missing user, of no organization, and a valid one.
        await seed.InsertAsync("Organizations", new() { ["Name"] = "org", ["Created"] = now }, token).ConfigureAwait(false);
        var organization = await seed.MaxKeyAsync("Organizations", token).ConfigureAwait(false);
        await seed.InsertAsync("OrganizationMembers", new() { ["OrganizationKey"] = organization, ["UserId"] = User, ["IsAdmin"] = true }, token).ConfigureAwait(false);
        await seed.InsertAsync("OrganizationMembers", new() { ["OrganizationKey"] = organization, ["UserId"] = MissingUser, ["IsAdmin"] = false }, token).ConfigureAwait(false);
        await seed.InsertAsync("OrganizationMembers", new() { ["OrganizationKey"] = null, ["UserId"] = User, ["IsAdmin"] = false }, token).ConfigureAwait(false);

        await RunRow(seed, MissingConfig, now, token).ConfigureAwait(false);
        await seed.InsertAsync("ReplicationItems", new() { ["ConfigKey"] = MissingConfig, ["Name"] = "n", ["Version"] = "1", ["Part"] = string.Empty, ["Synced"] = now }, token).ConfigureAwait(false);
    }

    private static Task RepositoryRow(PreRepositoryDatabase.Inserter seed, string name, int? project, CancellationToken token)
        => seed.InsertAsync("Repositories", new()
        {
            ["Name"] = name, ["Format"] = "NuGet", ["Kind"] = "Local", ["AnonymousRead"] = true, ["Created"] = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), ["ProjectKey"] = project,
        }, token);

    private static Task Target(PreRepositoryDatabase.Inserter seed, string name, int project, CancellationToken token)
        => seed.InsertAsync("PermissionTargets", new() { ["Name"] = name, ["Created"] = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), ["IsBuiltIn"] = false, ["ProjectKey"] = project }, token);

    private static Task RunRow(PreRepositoryDatabase.Inserter seed, int config, DateTime now, CancellationToken token)
        => seed.InsertAsync("ReplicationRuns", new()
        {
            ["ConfigKey"] = config, ["Started"] = now, ["Status"] = 1, ["Examined"] = 0, ["Transferred"] = 0, ["Skipped"] = 0, ["Deleted"] = 0, ["Conflicts"] = 0, ["Failures"] = 0, ["Bytes"] = 0L,
        }, token);

    /// <summary>After the migrations ran: every orphan is gone (or detached), every valid row kept, nothing else touched.</summary>
    public static async Task AssertCleanedAsync(IContext context, CancellationToken token)
    {
        // Users and keys.
        Assert.Equal(new[] { "k-mixed", "k-ok" }, await context.ApiKeys!.Select(k => k.Description!).OrderBy(d => d).ToListAsync(token));
        var mixed = await context.ApiKeys!.SingleAsync(k => k.Description == "k-mixed", token);
        Assert.Equal(new[] { 1 }, await context.ApiKeyRepositories!.Where(r => r.ApiKeyKey == mixed.Key).Select(r => r.RepositoryKey).ToListAsync(token));
        Assert.Equal(1, await context.ApiKeyRepositories!.CountAsync(token));
        Assert.Equal(new[] { User }, await context.GroupMembers!.Select(m => m.UserId!).ToListAsync(token));

        // Projects: detached repository, deleted target.
        Assert.Null((await context.Repositories!.SingleAsync(r => r.Name == "detached", token)).ProjectKey);
        Assert.NotNull((await context.Repositories!.SingleAsync(r => r.Name == "kept", token)).ProjectKey);
        Assert.Equal(new[] { "target-ok" }, await context.PermissionTargets!.Where(t => !t.IsBuiltIn).Select(t => t.Name!).ToListAsync(token));

        // NuGet.
        Assert.Equal(new[] { "Valid.Package" }, await context.Packages!.IgnoreQueryFilters().Select(p => p.Id!).ToListAsync(token));
        Assert.Equal(1, await ((DbContext)context).Set<PackageType>().CountAsync(token));
        Assert.Equal(1, await ((DbContext)context).Set<TargetFramework>().CountAsync(token));
        Assert.Equal(1, await context.PackageDependencies!.CountAsync(token));
        Assert.Equal(new[] { "Valid.Package" }, await context.PackageValidations!.IgnoreQueryFilters().Select(v => v.PackageId!).ToListAsync(token));
        Assert.Equal(new[] { "Valid.Package" }, await context.PackageOwners!.IgnoreQueryFilters().Select(o => o.PackageId!).ToListAsync(token));

        // Policies.
        var retention = await context.RetentionPolicies!.OrderBy(p => p.Name).ToListAsync(token);
        Assert.Equal(new[] { "r-mixed", "r-ok" }, retention.Select(p => p.Name));
        Assert.All(retention, p => { Assert.Equal(1, p.RepositoryKey); Assert.Null(p.ProjectKey); });
        var scans = await context.ScanPolicies!.ToListAsync(token);
        Assert.Single(scans);
        Assert.Equal(1, scans[0].RepositoryKey);

        // Other formats and replication.
        Assert.Equal(new[] { "valid-npm" }, await context.NpmPackages!.IgnoreQueryFilters().Select(p => p.Name!).ToListAsync(token));
        Assert.Equal(1, await context.NpmVersions!.CountAsync(token));
        Assert.Equal(1, await context.NpmDistTags!.CountAsync(token));
        Assert.Equal(new[] { "valid/image" }, await context.OciRepositories!.IgnoreQueryFilters().Select(i => i.Name!).ToListAsync(token));
        Assert.Equal(1, await context.OciManifests!.CountAsync(token));
        Assert.Equal(1, await context.OciTags!.CountAsync(token));
        Assert.Equal(1, await context.OciBlobs!.IgnoreQueryFilters().CountAsync(token)); // the orphan blob is gone, the valid one stays
        Assert.Equal(new[] { "valid-scan" }, await context.ScanResults!.IgnoreQueryFilters().Select(r => r.Name!).ToListAsync(token));
        Assert.Equal(1, await context.ScanFindings!.CountAsync(token));
        Assert.Equal(new[] { "c-ok" }, await context.ReplicationConfigs!.Select(c => c.Name!).ToListAsync(token));
        Assert.Equal(1, await context.ReplicationRuns!.CountAsync(token));
        Assert.Equal(1, await context.ReplicationItems!.CountAsync(token));

        // Maven, catalog, abuse reports, symbols, holds, usage, deprecations, vulnerabilities, blob links, uploads, memberships.
        Assert.Equal(new[] { "g.valid" }, await context.MavenArtifacts!.IgnoreQueryFilters().Select(a => a.GroupId!).ToListAsync(token));
        Assert.Equal(1, await context.MavenVersions!.CountAsync(token));
        Assert.Equal(1, await context.MavenFiles!.CountAsync(token));
        Assert.Equal(new[] { "valid" }, await context.CatalogLeafs!.IgnoreQueryFilters().Select(c => c.PackageId!).ToListAsync(token));
        Assert.Equal(new[] { "valid" }, await context.AbuseReports!.IgnoreQueryFilters().Select(c => c.PackageId!).ToListAsync(token));
        Assert.Equal(new[] { "valid" }, await context.PackageSymbols!.IgnoreQueryFilters().Select(c => c.PackageId!).ToListAsync(token));
        Assert.Equal(new[] { "valid" }, await context.RetentionHolds!.IgnoreQueryFilters().Select(c => c.Name!).ToListAsync(token));
        Assert.Equal(new[] { "valid" }, await context.ArtifactUsages!.IgnoreQueryFilters().Select(c => c.Name!).ToListAsync(token));
        Assert.Equal(1, await ((DbContext)context).Set<PackageDeprecation>().CountAsync(token));
        Assert.Equal(1, await ((DbContext)context).Set<PackageVulnerability>().CountAsync(token));
        Assert.Equal(new[] { 1 }, await context.OciBlobs!.IgnoreQueryFilters().Select(b => b.RepositoryKey).ToListAsync(token));
        Assert.Equal(1, await context.OciBlobLinks!.CountAsync(token));
        Assert.Equal(new[] { "valid" }, await context.OciUploads!.Select(u => u.Reference!).ToListAsync(token));
        var member = Assert.Single(await context.OrganizationMembers!.ToListAsync(token));
        Assert.Equal((User, true), (member.UserId, member.IsAdmin));
    }

    /// <summary>
    /// A database with rows in repository 1 but no Repositories row 1 (migrated by an early AddRepositories, never started):
    /// <see cref="AssertDefaultRepositoryCreatedAsync"/> checks it after the migrations.
    /// </summary>
    public static async Task CreateWithoutDefaultRepositoryAsync(IDatabaseContextFactory factory, string connectionString, DatabaseOptions options, CancellationToken token)
    {
        await using var context = (DbContext)factory.Create(connectionString, options);
        context.GetService<IMigrator>().Migrate(LastBeforeForeignKeys(context));

        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(token).ConfigureAwait(false);
        var seed = new PreRepositoryDatabase.Inserter(connection, options.Type ?? string.Empty);
        var now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM " + seed.Quote("Repositories");
            await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        foreach (var (id, repository) in new[] { ("Kept.Package", 1), ("Orphan.Package", MissingRepository) })
        {
            await seed.InsertAsync("Packages", new()
            {
                ["Id"] = id, ["Version"] = "1.0.0", ["Authors"] = "[]", ["Downloads"] = 0L, ["HasReadme"] = false, ["Listed"] = true,
                ["Published"] = now, ["RequireLicenseAcceptance"] = false, ["Tags"] = "[]", ["IsPrerelease"] = false, ["SemVerLevel"] = 0,
                ["OriginalVersion"] = "1.0.0", ["HasEmbeddedIcon"] = false, ["RepositoryKey"] = repository,
            }, token).ConfigureAwait(false);
            await seed.InsertAsync("PackageOwners", new() { ["PackageId"] = id, ["OwnerName"] = "ok", ["IsVerified"] = false, ["RepositoryKey"] = repository }, token).ConfigureAwait(false);
        }
    }

    /// <summary>After the migrations: row 1 exists as <c>default</c>, the rows of repository 1 are kept, the other orphans are gone.</summary>
    public static async Task AssertDefaultRepositoryCreatedAsync(IContext context, CancellationToken token)
    {
        var repository = await context.Repositories!.SingleAsync(token);
        Assert.Equal((Repository.DefaultKey, Repository.DefaultName), (repository.Key, repository.Name));
        Assert.Equal(new[] { "Kept.Package" }, await context.Packages!.IgnoreQueryFilters().Select(p => p.Id!).ToListAsync(token));
        Assert.Equal(new[] { "Kept.Package" }, await context.PackageOwners!.IgnoreQueryFilters().Select(o => o.PackageId!).ToListAsync(token));

        // The key sequence continues after row 1 on every engine.
        var created = await new RepositoryService(context).CreateAsync("next", RepositoryFormat.NuGet, RepositoryKind.Local, token);
        Assert.Equal(Repository.DefaultKey + 1, created.Key);
    }
}
