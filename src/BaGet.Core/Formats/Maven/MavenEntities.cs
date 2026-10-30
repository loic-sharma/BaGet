using System;

namespace BaGet.Core;

/// <summary>
/// A Maven artifact (<c>groupId:artifactId</c>) of one repository: the facts shared by all its versions. Group and artifact
/// ids are case-sensitive, as Maven's layout is (the SQL Server and MySQL contexts compare them by code point).
/// </summary>
public class MavenArtifact : IRepositoryScoped
{
    public int Key { get; set; }

    /// <summary>The owning repository; see <see cref="IRepositoryScoped"/>.</summary>
    public int RepositoryKey { get; set; }

    /// <summary>The group id (<c>org.apache.maven</c>). Unique with <see cref="ArtifactId"/> per repository.</summary>
    public string? GroupId { get; set; }

    /// <summary>The artifact id (<c>maven-core</c>).</summary>
    public string? ArtifactId { get; set; }

    /// <summary>The first deploy.</summary>
    public DateTime Created { get; set; }

    /// <summary>The last change to any version or file.</summary>
    public DateTime Modified { get; set; }
}

/// <summary>
/// One version of a <see cref="MavenArtifact"/> (<c>1.2.3</c>, or <c>1.0-SNAPSHOT</c>, which holds the timestamped builds as
/// files). The POM facts are read from the <c>.pom</c> file when it is deployed and feed the search; the POM itself is a file.
/// </summary>
public class MavenVersion
{
    public int Key { get; set; }

    /// <summary>The <see cref="MavenArtifact.Key"/>. Required; deleting the artifact deletes its versions.</summary>
    public int ArtifactKey { get; set; }

    /// <summary>The version as it is in the path. Unique per artifact.</summary>
    public string? Version { get; set; }

    /// <summary>Whether the version ends in <c>-SNAPSHOT</c>.</summary>
    public bool IsSnapshot { get; set; }

    /// <summary>The POM's <c>name</c> (cut to the column), or null.</summary>
    public string? Name { get; set; }

    /// <summary>The POM's <c>description</c> (cut to the column), or null.</summary>
    public string? Description { get; set; }

    /// <summary>The POM's <c>packaging</c> (<c>jar</c> when the POM says nothing), or null before a POM was deployed.</summary>
    public string? Packaging { get; set; }

    /// <summary>The first file of the version.</summary>
    public DateTime Created { get; set; }

    /// <summary>The last file added or replaced.</summary>
    public DateTime Modified { get; set; }
}

/// <summary>
/// A file of a <see cref="MavenVersion"/>: an artifact (<c>a-1.0.jar</c>, <c>a-1.0-sources.jar</c>), a POM or a signature. The
/// bytes live in storage (<c>maven/{group}/{artifact}/{version}/{file}</c> under the repository's prefix); checksums are
/// computed on the way in and served from here.
/// </summary>
public class MavenFile
{
    public int Key { get; set; }

    /// <summary>The <see cref="MavenVersion.Key"/>. Required; deleting the version deletes its files.</summary>
    public int VersionKey { get; set; }

    /// <summary>The file name as in the path. Unique per version.</summary>
    public string? FileName { get; set; }

    /// <summary>The classifier (<c>sources</c>), or null.</summary>
    public string? Classifier { get; set; }

    /// <summary>The extension without the dot (<c>jar</c>, <c>tar.gz</c>, <c>jar.asc</c>).</summary>
    public string? Extension { get; set; }

    /// <summary>For a timestamped snapshot build: its <c>yyyyMMdd.HHmmss</c> UTC timestamp; otherwise null.</summary>
    public string? SnapshotTimestamp { get; set; }

    /// <summary>For a timestamped snapshot build: its build number; otherwise 0.</summary>
    public int SnapshotBuild { get; set; }

    /// <summary>Bytes stored.</summary>
    public long Size { get; set; }

    /// <summary>SHA-1, lower-case hex.</summary>
    public string? Sha1 { get; set; }

    /// <summary>MD5, lower-case hex.</summary>
    public string? Md5 { get; set; }

    /// <summary>SHA-256, lower-case hex.</summary>
    public string? Sha256 { get; set; }

    /// <summary>SHA-512, lower-case hex.</summary>
    public string? Sha512 { get; set; }

    /// <summary>When the file was stored.</summary>
    public DateTime Created { get; set; }
}
