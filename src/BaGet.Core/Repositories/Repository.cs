using System;

namespace BaGet.Core;

/// <summary>
/// A named package repository. The unit every package, owner, catalog entry, validation and abuse report
/// belongs to, through its <see cref="IRepositoryScoped.RepositoryKey"/>.
/// </summary>
/// <remarks>
/// Format-specific metadata (a future npm or Maven table) hangs off <see cref="Key"/> in tables of its own, so
/// the common columns here stay format-neutral. The <see cref="DefaultName"/> repository (key
/// <see cref="DefaultKey"/>) is the one served at <c>/</c>; it is created at startup when missing and holds every
/// row that predates repositories.
/// </remarks>
public class Repository
{
    /// <summary>
    /// The name of the repository served at <c>/</c>.
    /// </summary>
    public const string DefaultName = "default";

    /// <summary>
    /// The key of the <see cref="DefaultName"/> repository: the value migrations give every pre-existing row.
    /// </summary>
    public const int DefaultKey = 1;

    /// <summary>
    /// The name of the remote repository created from the <c>Mirror:*</c> options when <c>Mirror:Enabled</c> is set;
    /// the default repository fetches through it (see <see cref="IRemoteUpstreamSource"/>).
    /// </summary>
    public const string MirrorName = "mirror";

    public int Key { get; set; }

    /// <summary>
    /// Unique, and valid per <see cref="RepositoryNames"/>. It appears in URLs and storage paths, so it is
    /// never renamed.
    /// </summary>
    public string? Name { get; set; }

    public RepositoryFormat Format { get; set; }

    public RepositoryKind Kind { get; set; }

    /// <summary>
    /// The <see cref="Project.Key"/> that owns the repository, if any. A Restrict foreign key to <see cref="Project"/> (nullable);
    /// deleting the project clears it first.
    /// </summary>
    public int? ProjectKey { get; set; }

    /// <summary>
    /// Format and kind specific settings as JSON: a remote's upstream (<see cref="RemoteRepositorySettings"/>, read
    /// and written by <see cref="RemoteSettingsCodec"/> only, as it holds a protected password), later a virtual's
    /// members. Opaque to the common code.
    /// </summary>
    public string? Settings { get; set; }

    /// <summary>
    /// Whether requests without credentials may read the repository.
    /// </summary>
    public bool AnonymousRead { get; set; } = true;

    public DateTime Created { get; set; }

    /// <summary>
    /// The immutable view of this row that <see cref="IRepositoryContext"/> carries.
    /// </summary>
    public RepositoryInfo ToInfo() => new(Key, Name ?? string.Empty, Format, Kind);
}

/// <summary>
/// A group of repositories with its own administrators (Artifactory's projects).
/// </summary>
public class Project
{
    public int Key { get; set; }

    /// <summary>
    /// Unique; lowercase letters, digits and hyphens (Artifactory's "project key").
    /// </summary>
    public string? Name { get; set; }

    public string? DisplayName { get; set; }

    public DateTime Created { get; set; }
}

/// <summary>
/// An entity that belongs to one <see cref="Repository"/>. The context filters every query on these by the
/// current repository (<see cref="IRepositoryContext"/>) and stamps new rows with it. <c>RepositoryKey</c> is a Restrict foreign key
/// to <see cref="Repository"/>: the code that deletes a repository removes the rows first.
/// </summary>
public interface IRepositoryScoped
{
    /// <summary>
    /// The owning <see cref="Repository.Key"/>. Left 0 on a new row, it is set to the current repository on save.
    /// </summary>
    int RepositoryKey { get; set; }
}
