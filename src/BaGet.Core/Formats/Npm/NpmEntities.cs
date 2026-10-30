using System;

namespace BaGet.Core;

/// <summary>
/// An npm package (a packument) of one repository: the name and the facts shared by all its versions. Scoped names keep
/// the scope (<c>@scope/name</c>). Written by the npm format module of a later phase; the tables exist from the format layer
/// on so the data model is settled on all four providers before any protocol code depends on it.
/// </summary>
public class NpmPackage : IRepositoryScoped
{
    public int Key { get; set; }

    /// <summary>
    /// The owning repository; see <see cref="IRepositoryScoped"/>.
    /// </summary>
    public int RepositoryKey { get; set; }

    /// <summary>
    /// The full package name, lower-cased (npm only allows lower case for new names): <c>left-pad</c> or
    /// <c>@scope/name</c>. At most <c>AbstractContext.MaxNpmNameLength</c> characters, npm's own limit. Unique per repository.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>The first publish (the packument's <c>time.created</c>).</summary>
    public DateTime Created { get; set; }

    /// <summary>The last change to any version or dist-tag (the packument's <c>time.modified</c>).</summary>
    public DateTime Modified { get; set; }
}

/// <summary>
/// One published version of an <see cref="NpmPackage"/>. The tarball is not in the row: it lives in storage
/// (<c>npm/{name}/-/{file}</c> under the repository's prefix) and this row records what a client verifies it with.
/// </summary>
public class NpmVersion
{
    public int Key { get; set; }

    /// <summary>The <see cref="NpmPackage.Key"/>. Required; deleting the package deletes its versions.</summary>
    public int PackageKey { get; set; }

    /// <summary>The semver version as published (npm compares versions as semver, build metadata ignored). Unique per package.</summary>
    public string? Version { get; set; }

    /// <summary>The Subresource Integrity string of the tarball (<c>sha512-…</c>), as clients check it.</summary>
    public string? Integrity { get; set; }

    /// <summary>The SHA-1 of the tarball in hex (<c>dist.shasum</c>), still sent by every npm client.</summary>
    public string? Shasum { get; set; }

    /// <summary>The tarball's size in bytes.</summary>
    public long TarballSize { get; set; }

    /// <summary>
    /// The version manifest (the <c>package.json</c> object the client sent for this version, plus the server's <c>dist</c>), as
    /// JSON. Unbounded: it carries dependencies, scripts, readme and whatever the publisher put in. The packument is built from
    /// these, so the registry answers exactly what was published.
    /// </summary>
    public string? Manifest { get; set; }

    /// <summary>The deprecation message set with <c>npm deprecate</c>; null when not deprecated. An empty message un-deprecates, so it is stored as null.</summary>
    public string? Deprecated { get; set; }

    public DateTime Published { get; set; }
}

/// <summary>
/// A dist-tag of an <see cref="NpmPackage"/> (<c>latest</c>, <c>next</c>…): a name that points to a version string. Unique per
/// package and tag. There is no foreign key to <see cref="NpmVersion"/>: the format module keeps the tag and its version in step
/// in one save (unpublishing a version moves or removes its tags).
/// </summary>
public class NpmDistTag
{
    public int Key { get; set; }

    /// <summary>The <see cref="NpmPackage.Key"/>. Required; deleting the package deletes its tags.</summary>
    public int PackageKey { get; set; }

    /// <summary>The tag name; npm forbids names that are valid semver ranges.</summary>
    public string? Tag { get; set; }

    /// <summary>The <see cref="NpmVersion.Version"/> the tag points to.</summary>
    public string? Version { get; set; }
}
