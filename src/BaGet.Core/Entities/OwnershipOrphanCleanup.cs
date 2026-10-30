using System;
using System.Collections.Generic;

namespace BaGet.Core;

/// <summary>The engines <see cref="OwnershipOrphanCleanup"/> writes SQL for.</summary>
public enum OwnershipEngine
{
    Sqlite,
    SqlServer,
    PostgreSql,
    MySql,
}

/// <summary>
/// The SQL the <c>AddOwnershipForeignKeys</c> migration of every provider runs <b>before</b> it adds the foreign keys: rows that
/// already pointed at a repository, project, user or replication configuration that does not exist are removed (or, for an
/// optional reference, detached), children before parents, so the constraints can be created. The statements are ANSI
/// (<c>DELETE ... WHERE ... IN (SELECT ...)</c>, never a subquery on the table being changed, which MySQL refuses); only the
/// identifier quoting differs by engine.
/// </summary>
/// <remarks>
/// Frozen for that migration: it names the tables and columns as they were then, so it must not follow later schema changes.
/// Decisions: owned data of a missing repository is deleted with everything under it; an API key limited only to missing
/// repositories is deleted (its rows going would turn it unlimited); a policy whose only target is gone is deleted, one that
/// still has another target is detached from the missing one; a permission target administered by a missing project is deleted
/// (detaching would widen it to every repository); a repository of a missing project is detached from it.
/// </remarks>
public static class OwnershipOrphanCleanup
{
    /// <param name="engine">The engine the statements run on.</param>
    public static IReadOnlyList<string> Statements(OwnershipEngine engine)
    {
        Func<string, string> q = engine switch
        {
            OwnershipEngine.SqlServer => name => "[" + name + "]",
            OwnershipEngine.MySql => name => "`" + name + "`",
            _ => name => "\"" + name + "\"",
        };
        var statements = new List<string>();
        void Run(string sql) => statements.Add(sql);

        // The default repository (key 1) may be missing: a database migrated by an early AddRepositories (before the migration
        // inserted the row) and not started since. Every row of key 1 belongs to it, so the row is created first, as
        // AddRepositories and RepositoryService.EnsureDefaultAsync do, and key 1 is never treated as missing below.
        var now = engine switch
        {
            OwnershipEngine.SqlServer => "SYSUTCDATETIME()",
            OwnershipEngine.PostgreSql => "now()",
            OwnershipEngine.MySql => "UTC_TIMESTAMP(6)",
            _ => "CURRENT_TIMESTAMP",
        };
        var yes = engine == OwnershipEngine.PostgreSql ? "TRUE" : "1";
        var dual = engine == OwnershipEngine.MySql ? " FROM DUAL" : string.Empty;
        var insertDefault =
            $"INSERT INTO {q("Repositories")} ({q("Key")}, {q("Name")}, {q("Format")}, {q("Kind")}, {q("AnonymousRead")}, {q("Created")}) " +
            $"SELECT {Repository.DefaultKey}, '{Repository.DefaultName}', 'NuGet', 'Local', {yes}, {now}{dual} " +
            $"WHERE NOT EXISTS (SELECT 1 FROM {q("Repositories")} WHERE {q("Key")} = {Repository.DefaultKey} OR {q("Name")} = '{Repository.DefaultName}')";
        if (engine == OwnershipEngine.SqlServer) Run($"SET IDENTITY_INSERT {q("Repositories")} ON");
        Run(insertDefault);
        if (engine == OwnershipEngine.SqlServer) Run($"SET IDENTITY_INSERT {q("Repositories")} OFF");
        if (engine == OwnershipEngine.PostgreSql)
        {
            Run($"SELECT setval(pg_get_serial_sequence('\"Repositories\"', 'Key'), (SELECT MAX({q("Key")}) FROM {q("Repositories")}))");
        }

        string Repos = $"SELECT {q("Key")} FROM {q("Repositories")}";
        string Missing(string column) => $"({q(column)} <> {Repository.DefaultKey} AND {q(column)} NOT IN ({Repos}))";
        string Delete(string table, string where) => $"DELETE FROM {q(table)} WHERE {where}";

        // Users: keys (their repository limits go with them), memberships.
        var noUser = $"{q("UserId")} NOT IN (SELECT {q("Id")} FROM {q("AspNetUsers")})";
        Run(Delete("ApiKeys", noUser));
        Run(Delete("GroupMembers", noUser));
        Run(Delete("OrganizationMembers", noUser));
        // A membership of no organization (the key is optional) is nothing; the user's foreign key would otherwise be the only link.
        Run(Delete("OrganizationMembers", $"{q("OrganizationKey")} IS NULL"));

        // API keys limited only to missing repositories would become unlimited: delete them (the limit rows cascade).
        Run(Delete("ApiKeys",
            $"{q("Key")} IN (SELECT {q("ApiKeyKey")} FROM {q("ApiKeyRepositories")} WHERE {Missing("RepositoryKey")}) " +
            $"AND {q("Key")} NOT IN (SELECT {q("ApiKeyKey")} FROM {q("ApiKeyRepositories")} WHERE {q("RepositoryKey")} IN ({Repos}))"));
        Run(Delete("ApiKeyRepositories", Missing("RepositoryKey")));

        // NuGet: a missing repository's packages and what hangs from them.
        var orphanPackages = $"SELECT {q("Key")} FROM {q("Packages")} WHERE {Missing("RepositoryKey")}";
        foreach (var child in new[] { "PackageDependencies", "PackageTypes", "TargetFrameworks", "PackageDeprecations", "PackageVulnerabilities" })
        {
            Run(Delete(child, $"{q("PackageKey")} IN ({orphanPackages})"));
        }

        Run(Delete("PackageValidations", $"{q("PackageKey")} IN ({orphanPackages}) OR {Missing("RepositoryKey")}"));
        Run(Delete("Packages", Missing("RepositoryKey")));
        foreach (var table in new[] { "PackageOwners", "CatalogLeafs", "AbuseReports", "PackageSymbols" })
        {
            Run(Delete(table, Missing("RepositoryKey")));
        }

        // npm.
        var orphanNpm = $"SELECT {q("Key")} FROM {q("NpmPackages")} WHERE {Missing("RepositoryKey")}";
        Run(Delete("NpmVersions", $"{q("PackageKey")} IN ({orphanNpm})"));
        Run(Delete("NpmDistTags", $"{q("PackageKey")} IN ({orphanNpm})"));
        Run(Delete("NpmPackages", Missing("RepositoryKey")));

        // Maven.
        var orphanArtifacts = $"SELECT {q("Key")} FROM {q("MavenArtifacts")} WHERE {Missing("RepositoryKey")}";
        var orphanVersions = $"SELECT {q("Key")} FROM {q("MavenVersions")} WHERE {q("ArtifactKey")} IN ({orphanArtifacts})";
        Run(Delete("MavenFiles", $"{q("VersionKey")} IN ({orphanVersions})"));
        Run(Delete("MavenVersions", $"{q("ArtifactKey")} IN ({orphanArtifacts})"));
        Run(Delete("MavenArtifacts", Missing("RepositoryKey")));

        // OCI: images of a missing repository (and tags of an image that does not exist, which had no constraint), their
        // manifests, links, upload sessions; then blobs of a missing repository (links first: the blob side restricts).
        var orphanImages = $"SELECT {q("Key")} FROM {q("OciRepositories")} WHERE {Missing("RepositoryKey")}";
        var noImage = $"{q("ImageKey")} NOT IN (SELECT {q("Key")} FROM {q("OciRepositories")})";
        var orphanBlobs = $"SELECT {q("Key")} FROM {q("OciBlobs")} WHERE {Missing("RepositoryKey")}";
        Run(Delete("OciTags", $"{noImage} OR {q("ImageKey")} IN ({orphanImages})"));
        Run(Delete("OciBlobLinks", $"{q("ImageKey")} IN ({orphanImages}) OR {q("BlobKey")} IN ({orphanBlobs})"));
        Run(Delete("OciUploads", $"{q("ImageKey")} IN ({orphanImages})"));
        Run(Delete("OciManifests", $"{q("ImageKey")} IN ({orphanImages})"));
        Run(Delete("OciRepositories", Missing("RepositoryKey")));
        Run(Delete("OciBlobs", Missing("RepositoryKey")));

        // Scanning, retention, replication.
        Run(Delete("ScanFindings", $"{q("ResultKey")} IN (SELECT {q("Key")} FROM {q("ScanResults")} WHERE {Missing("RepositoryKey")})"));
        Run(Delete("ScanResults", Missing("RepositoryKey")));
        Run(Delete("RetentionHolds", Missing("RepositoryKey")));
        Run(Delete("ArtifactUsages", Missing("RepositoryKey")));

        var orphanConfigs = $"SELECT {q("Key")} FROM {q("ReplicationConfigs")} WHERE {Missing("RepositoryKey")}";
        var noConfig = $"{q("ConfigKey")} NOT IN (SELECT {q("Key")} FROM {q("ReplicationConfigs")})";
        foreach (var table in new[] { "ReplicationRuns", "ReplicationItems" })
        {
            Run(Delete(table, $"{noConfig} OR {q("ConfigKey")} IN ({orphanConfigs})"));
        }

        Run(Delete("ReplicationConfigs", Missing("RepositoryKey")));

        // Nullable references: a policy keeps meaning only while one of its targets exists.
        var projects = $"SELECT {q("Key")} FROM {q("Projects")}";
        string RepoGone = $"({q("RepositoryKey")} IS NOT NULL AND {Missing("RepositoryKey")})";
        string ProjectGone = $"({q("ProjectKey")} IS NOT NULL AND {q("ProjectKey")} NOT IN ({projects}))";
        foreach (var table in new[] { "RetentionPolicies", "ScanPolicies" })
        {
            Run(Delete(table,
                $"({RepoGone} AND ({q("ProjectKey")} IS NULL OR {ProjectGone})) OR ({ProjectGone} AND {q("RepositoryKey")} IS NULL)"));
            Run($"UPDATE {q(table)} SET {q("RepositoryKey")} = NULL WHERE {RepoGone}");
            Run($"UPDATE {q(table)} SET {q("ProjectKey")} = NULL WHERE {ProjectGone}");
        }

        Run(Delete("PermissionTargets", ProjectGone));
        Run($"UPDATE {q("Repositories")} SET {q("ProjectKey")} = NULL WHERE {ProjectGone}");

        return statements;
    }
}
